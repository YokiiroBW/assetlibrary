using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Application;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.OperationTrash.Domain;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Infrastructure;

/// <summary>
/// Real, read-only inspection of one prepared inbound media package inside its trusted staging tree. It
/// creates nothing, writes nothing and grants nothing: it anchors both trusted roots, aligns the
/// declared file set with the real listing, streams and hashes every declared file, and re-checks the
/// authorization scope before each read and before it returns.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "This is the module's single inspection orchestrator: the type count is the shape of one ordered preflight (two trusted roots, declared set versus real listing, per-file hash, capacity, revocation) plus the environment-failure types it must translate into named codes. The directory walk lives in MediaPackagePathBoundary, the raw-byte reading in MediaPackageFileHasher and the verdicts in MediaPackagePolicy, so no rule is duplicated here and no other module's infrastructure is reached.")]
public sealed class IsolatedMediaPackageInspector(
    IMediaPackageInspectionScopeQuery scopeQuery,
    IMediaPackageFileHasher fileHasher,
    IMediaPackageVolumeSpaceObserver spaceObserver) : IMediaPackageInspectionPort
{
    public async ValueTask<MediaPackageInspectionOutcome> InspectAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(limits);
        var issues = new MediaPackageIssueSink(limits.MaximumIssueCount);
        try
        {
            var load = await InspectCoreAsync(
                scope,
                manifest,
                limits,
                issues,
                cancellationToken).ConfigureAwait(false);
            return new MediaPackageInspectionOutcome(
                scope,
                load?.Files ?? [],
                load?.VerifiedBytes ?? 0,
                0,
                issues);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a package verdict and must reach the caller unchanged.
            throw;
        }
        catch (Exception exception) when (IsEnvironmentFailure(exception))
        {
            // A named environment failure replaces the raw framework exception.
            issues.Record("io_failure");
            return new MediaPackageInspectionOutcome(scope, [], 0, 0, issues);
        }
    }

    private static bool IsEnvironmentFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;

    /// <summary>
    /// Returns the verified payload set, or null when the package was already refused and the reason
    /// has been recorded in <paramref name="issues"/>.
    /// </summary>
    private async ValueTask<PackageLoad?> InspectCoreAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        CancellationToken cancellationToken)
    {
        var rootFault = TryOpenRoots(scope, out var stagingBoundary, out var targetBoundary);
        if (rootFault is not null)
        {
            return Refuse(issues, rootFault, "scope");
        }

        if (stagingBoundary!.Overlaps(stagingBoundary.CanonicalRoot, targetBoundary!.CanonicalRoot))
        {
            return Refuse(issues, "unsafe_path", "scope");
        }

        // The package must be one directory directly inside the trusted staging root: the manifest can
        // never widen that to an arbitrary path.
        if (!TryObservePackageRoot(
                stagingBoundary,
                manifest,
                out var packageRoot,
                out var packageFault))
        {
            return Refuse(issues, packageFault, manifest.StagingRef);
        }

        var targetName = MediaPackagePolicy.TargetDirectoryName(manifest);
        if (!targetBoundary.TryResolveChild(
                targetBoundary.CanonicalRoot,
                targetName,
                out var targetDirectory,
                out var targetFault))
        {
            return Refuse(issues, MediaPackagePathBoundary.FaultCode(targetFault), targetName);
        }

        if (targetBoundary.TryObserve(targetDirectory, out _, out _))
        {
            // Any existing object at the target name is a conflict. Contents are never compared and
            // never reused.
            return Refuse(issues, "target_exists", targetName);
        }

        // Target absence is only acceptable when the trusted parent root really exists: absence is
        // observed through the parent, and nothing is created to make it observable.
        if (!targetBoundary.TryObserve(targetBoundary.CanonicalRoot, out _, out var parentFault)
            || parentFault != MediaPackagePathFault.None)
        {
            return Refuse(
                issues,
                parentFault == MediaPackagePathFault.None
                    ? "target_unavailable"
                    : MediaPackagePathBoundary.FaultReason(parentFault, parentFault, "target_unavailable"),
                targetName);
        }

        return await LoadAsync(
            scope,
            manifest,
            limits,
            issues,
            stagingBoundary,
            packageRoot,
            targetDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PackageLoad?> LoadAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        MediaPackagePathBoundary stagingBoundary,
        string packageRoot,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var declaredBytes = DeclaredByteCount(manifest);
        if (!await HasTargetCapacityAsync(
                targetDirectory,
                declaredBytes,
                limits,
                issues,
                cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var listing = stagingBoundary.Enumerate(packageRoot, issues, cancellationToken);
        if (issues.HasIssues
            || !MediaPackagePolicy.ValidateFileSet(
                manifest,
                listing.Files,
                listing.Directories,
                issues))
        {
            return null;
        }

        var observed = await ReadDeclaredFilesAsync(
            scope,
            manifest,
            limits,
            issues,
            stagingBoundary,
            packageRoot,
            cancellationToken).ConfigureAwait(false);
        if (observed is null)
        {
            return null;
        }

        // A second listing proves the final file set: a file added while the bytes were being read is a
        // change, not a detail to ignore. Directories are compared too, so an empty directory cannot
        // appear unnoticed.
        var confirm = stagingBoundary.Enumerate(packageRoot, issues, cancellationToken);
        if (!SameListing(listing, confirm))
        {
            return Refuse(issues, "source_changed", manifest.StagingRef);
        }

        if (!await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            return Refuse(issues, "scope_changed", null);
        }

        return observed;
    }

    private async ValueTask<PackageLoad?> ReadDeclaredFilesAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        MediaPackagePathBoundary stagingBoundary,
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var observed = new List<MediaPackageObservedFile>(manifest.Files.Count);
        long verifiedBytes = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false))
            {
                return Refuse(issues, "scope_changed", file.Path);
            }

            var remaining = limits.MaximumReadByteCount - verifiedBytes;
            if (remaining <= 0)
            {
                return Refuse(issues, "budget_exceeded", file.Path);
            }

            var facts = await ReadAndRecheckAsync(
                stagingBoundary,
                packageRoot,
                file,
                remaining,
                issues,
                cancellationToken).ConfigureAwait(false);
            if (facts is null)
            {
                return null;
            }

            if (facts.Length != file.SizeBytes)
            {
                return Refuse(issues, "size_mismatch", file.Path);
            }

            if (facts.Sha256 != file.Sha256)
            {
                return Refuse(issues, "hash_mismatch", file.Path);
            }

            verifiedBytes += facts.Length;
            observed.Add(new MediaPackageObservedFile(file.Path, file.Kind, file.Cid, facts));
        }

        return new PackageLoad(observed, verifiedBytes);
    }

    /// <summary>
    /// Resolves, observes, re-observes and hashes one declared file. The stamp taken before and after the
    /// read is what makes a quiet substitution during the read a verdict instead of a silent pass.
    /// </summary>
    private async ValueTask<PayloadFacts?> ReadAndRecheckAsync(
        MediaPackagePathBoundary stagingBoundary,
        string packageRoot,
        MediaPackageFileEntry file,
        long remaining,
        MediaPackageIssueSink issues,
        CancellationToken cancellationToken)
    {
        var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
        var resolved = stagingBoundary.TryResolveChild(
            packageRoot,
            relative,
            out var absolutePath,
            out var resolveFault);
        if (!resolved)
        {
            issues.Record(MediaPackagePathBoundary.FaultCode(resolveFault), file.Path);
            return null;
        }

        if (!stagingBoundary.TryObserve(absolutePath, out var length, out var observeFault))
        {
            issues.Record(
                MediaPackagePathBoundary.FaultReason(
                    MediaPackagePathFault.None,
                    observeFault,
                    "source_missing"),
                file.Path);
            return null;
        }

        if (!stagingBoundary.Contains(absolutePath, packageRoot))
        {
            issues.Record("unsafe_path", file.Path);
            return null;
        }

        var before = MediaPackagePathBoundary.Stamp(absolutePath);
        var facts = await fileHasher
            .HashAsync(absolutePath, remaining, cancellationToken)
            .ConfigureAwait(false);
        var after = MediaPackagePathBoundary.Stamp(absolutePath);
        if (after != before || length != facts.Length)
        {
            issues.Record("source_changed", file.Path);
            return null;
        }

        return facts;
    }

    private static bool TryObservePackageRoot(
        MediaPackagePathBoundary stagingBoundary,
        MediaPackageManifest manifest,
        out string packageRoot,
        out string faultCode)
    {
        var resolved = stagingBoundary.TryResolveChild(
            stagingBoundary.CanonicalRoot,
            manifest.StagingRef,
            out packageRoot,
            out var resolveFault);
        if (!resolved)
        {
            faultCode = MediaPackagePathBoundary.FaultCode(resolveFault);
            return false;
        }

        if (stagingBoundary.TryObserve(packageRoot, out _, out var observeFault))
        {
            faultCode = string.Empty;
            return true;
        }

        faultCode = MediaPackagePathBoundary.FaultCode(
            observeFault == MediaPackagePathFault.None
                ? MediaPackagePathFault.Missing
                : observeFault);
        return false;
    }

    /// <summary>
    /// Anchors both trusted roots. Returns the fault code to report, or null when both are usable.
    /// </summary>
    private static string? TryOpenRoots(
        MediaPackageInspectionScope scope,
        out MediaPackagePathBoundary? stagingBoundary,
        out MediaPackagePathBoundary? targetBoundary)
    {
        if (!MediaPackagePathBoundary.TryOpen(
                scope.StagingRoot.Value,
                new CanonicalLibraryRoot(scope.StagingRoot.Value, scope.StagingRoot.Comparison),
                out stagingBoundary,
                out var fault))
        {
            targetBoundary = null;
            return MediaPackagePathBoundary.FaultCode(fault);
        }

        return MediaPackagePathBoundary.TryOpen(
            scope.TargetLibraryRoot.Value,
            new CanonicalLibraryRoot(
                scope.TargetLibraryRoot.Value,
                scope.TargetLibraryRoot.Comparison),
            out targetBoundary,
            out fault)
            ? null
            : MediaPackagePathBoundary.FaultCode(fault);
    }

    private static PackageLoad? Refuse(
        MediaPackageIssueSink issues,
        string code,
        string? location)
    {
        issues.Record(code, location);
        return null;
    }

    private static long DeclaredByteCount(MediaPackageManifest manifest)
    {
        long declaredBytes = 0;
        foreach (var file in manifest.Files)
        {
            declaredBytes = declaredBytes > long.MaxValue - file.SizeBytes
                ? long.MaxValue
                : declaredBytes + file.SizeBytes;
        }

        return declaredBytes;
    }

    private static bool SameListing(
        MediaPackagePathBoundary.PackageListing left,
        MediaPackagePathBoundary.PackageListing right)
    {
        static string[] Sorted(IReadOnlyList<string> values) =>
            [.. values.OrderBy(value => value, StringComparer.Ordinal)];
        return Sorted(left.Files).SequenceEqual(Sorted(right.Files))
            && Sorted(left.Directories).SequenceEqual(Sorted(right.Directories));
    }

    private async ValueTask<bool> HasTargetCapacityAsync(
        string targetDirectory,
        long declaredBytes,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        CancellationToken cancellationToken)
    {
        long? available;
        try
        {
            available = await spaceObserver
                .ObserveAvailableBytesAsync(targetDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsEnvironmentFailure(exception))
        {
            available = null;
        }

        if (available is null)
        {
            // An unobservable volume is refused; sufficiency is never assumed.
            issues.Record("target_unavailable", "target");
            return false;
        }

        var required = declaredBytes > long.MaxValue - limits.MinimumTargetHeadroomByteCount
            ? long.MaxValue
            : declaredBytes + limits.MinimumTargetHeadroomByteCount;
        if (available.Value < required)
        {
            issues.Record("insufficient_space", "target");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The verified payload set of one accepted package.
    /// </summary>
    private sealed record PackageLoad(
        IReadOnlyList<MediaPackageObservedFile> Files,
        long VerifiedBytes);
}
