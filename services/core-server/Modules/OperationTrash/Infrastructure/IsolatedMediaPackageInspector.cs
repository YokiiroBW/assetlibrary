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
                [.. load.Files.Select(file => file.Entry)],
                load.VerifiedBytes,
                load.EnumeratedEntries,
                issues);
        }
        catch (InspectionRefusal refusal)
        {
            // A package-level refusal is a verdict, never an exception: it is recorded in the sink and
            // reported with the reason the ordered preflight reached. A refusal without a code already
            // recorded its own reason in the sink.
            if (refusal.Code is { } code)
            {
                issues.Record(code, refusal.Location);
            }

            return new MediaPackageInspectionOutcome(scope, [], 0, refusal.EnumeratedEntries, issues);
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

    /// <summary>
    /// Requires the trusted target parent root to be an observable directory. A regular file is not a
    /// parent: a target that "does not exist" underneath a file says nothing about the target, so it is
    /// refused rather than reported as a conflict. A root that is missing is unavailable, and a root that
    /// exists but cannot be observed is unsafe, so an unobservable object is never read as an absence.
    /// The same requirement is applied before the first read and again at the final confirmation.
    /// </summary>
    private static void RequireTargetParentDirectory(
        MediaPackagePathBoundary targetBoundary,
        string location,
        int enumeratedEntries = 0)
    {
        var isDirectory = targetBoundary.TryObserveDirectory(
            targetBoundary.CanonicalRoot,
            out var fault);
        if (isDirectory is true)
        {
            return;
        }

        throw new InspectionRefusal(
            isDirectory is false || fault != MediaPackagePathFault.Missing
                ? MediaPackagePathBoundary.FaultCode(
                    fault == MediaPackagePathFault.None ? MediaPackagePathFault.Unsafe : fault)
                : "target_unavailable",
            location,
            enumeratedEntries);
    }

    private static bool IsEnvironmentFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;

    /// <summary>
    /// Returns the verified payload set, or throws <see cref="InspectionRefusal"/> with the named reason
    /// when the package is refused. The refusal carries the entries already consumed, so the reported
    /// observation stays truthful about what the preflight really did.
    /// </summary>
    private async ValueTask<PackageLoad> InspectCoreAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        CancellationToken cancellationToken)
    {
        var rootFault = TryOpenRoots(scope, out var stagingBoundary, out var targetBoundary);
        if (rootFault is not null)
        {
            throw new InspectionRefusal(rootFault, "scope");
        }

        if (stagingBoundary!.Overlaps(stagingBoundary.CanonicalRoot, targetBoundary!.CanonicalRoot))
        {
            throw new InspectionRefusal("unsafe_path", "scope");
        }

        // The package must be one directory directly inside the trusted staging root: the manifest can
        // never widen that to an arbitrary path. A missing package root and an unavailable one are
        // different verdicts, because absence is observed through the trusted parent.
        if (!TryObservePackageRoot(stagingBoundary, manifest, out var packageRoot, out var packageFault))
        {
            throw new InspectionRefusal(packageFault, manifest.StagingRef);
        }

        var targetName = MediaPackagePolicy.TargetDirectoryName(manifest);
        if (!targetBoundary.TryResolveChild(
                targetBoundary.CanonicalRoot,
                targetName,
                out var targetDirectory,
                out var targetFault))
        {
            throw new InspectionRefusal(MediaPackagePathBoundary.FaultCode(targetFault), targetName);
        }

        // Any existing object at the target name is a conflict, whether it is a file, a directory or a
        // link. An object that cannot be observed at all is unsafe rather than absent: a refusal is never
        // read as an absence.
        if (targetBoundary.TryObserve(targetDirectory, out _, out var targetObserveFault))
        {
            throw new InspectionRefusal("target_exists", targetName);
        }

        if (targetObserveFault != MediaPackagePathFault.Missing)
        {
            throw new InspectionRefusal(
                MediaPackagePathBoundary.FaultCode(targetObserveFault),
                targetName);
        }

        // Target absence is only acceptable when the trusted parent root really exists and really is a
        // directory: absence is observed through the parent, and a regular file cannot be a parent. A
        // parent that is missing is unavailable, a parent that is not a directory or that cannot be
        // observed is unsafe, and nothing is created to make the environment observable.
        RequireTargetParentDirectory(targetBoundary, targetName);

        return await LoadAsync(
            scope,
            manifest,
            limits,
            issues,
            stagingBoundary,
            targetBoundary,
            packageRoot,
            targetDirectory,
            targetName,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PackageLoad> LoadAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        MediaPackagePathBoundary stagingBoundary,
        MediaPackagePathBoundary targetBoundary,
        string packageRoot,
        string targetDirectory,
        string targetName,
        CancellationToken cancellationToken)
    {
        var declaredBytes = DeclaredByteCount(manifest);
        if (await TargetCapacityRefusalAsync(
                targetDirectory,
                declaredBytes,
                limits,
                cancellationToken).ConfigureAwait(false) is { } capacityRefusal)
        {
            // Space that was observed to be too small and space that could not be observed at all are
            // different verdicts, and only the observed one may claim insufficiency.
            throw new InspectionRefusal(capacityRefusal, "target");
        }

        var listing = stagingBoundary.Enumerate(packageRoot, issues, cancellationToken);
        if (issues.HasIssues
            || !MediaPackagePolicy.ValidateFileSet(
                manifest,
                listing.Files,
                listing.Directories,
                issues))
        {
            throw new InspectionRefusal(null, null, listing.EnumeratedEntries);
        }

        var observed = await ReadDeclaredFilesAsync(
            scope,
            manifest,
            limits,
            issues,
            stagingBoundary,
            packageRoot,
            listing.EnumeratedEntries,
            cancellationToken).ConfigureAwait(false);

        // A second listing proves the final file set: a file added while the bytes were being read is a
        // change, not a detail to ignore. Directories are compared too, so an empty directory cannot
        // appear unnoticed.
        var confirm = stagingBoundary.Enumerate(packageRoot, issues, cancellationToken);
        if (issues.HasIssues || !SameListing(listing, confirm))
        {
            throw new InspectionRefusal("source_changed", manifest.StagingRef, confirm.EnumeratedEntries);
        }

        // Every file verified earlier is re-observed after the last read, so a file that changed while a
        // later file was being read cannot pass on the strength of its own earlier stamp. Length alone is
        // not enough: an equal-length rewrite changes the last-write stamp and must be refused.
        ReverifyObserved(stagingBoundary, packageRoot, observed.Files, issues);

        // The target must still be absent, and the absence must be observed rather than assumed: an
        // object that appeared is a conflict, and an object that cannot be observed at all is unsafe.
        if (targetBoundary.TryObserve(targetDirectory, out _, out var finalTargetFault))
        {
            throw new InspectionRefusal("target_exists", targetName, confirm.EnumeratedEntries);
        }

        if (finalTargetFault != MediaPackagePathFault.Missing)
        {
            throw new InspectionRefusal(
                MediaPackagePathBoundary.FaultCode(finalTargetFault),
                targetName,
                confirm.EnumeratedEntries);
        }

        // The trusted target parent root is re-checked at the end: if it disappeared or became
        // unobservable during the run, absence can no longer be proven through it and the target is
        // unavailable. Nothing is created to repair that.
        if (!MediaPackagePathBoundary.TryOpen(
                scope.TargetLibraryRoot.Value,
                new CanonicalLibraryRoot(
                    scope.TargetLibraryRoot.Value,
                    scope.TargetLibraryRoot.Comparison),
                out var reopenedTarget,
                out var parentFault))
        {
            throw new InspectionRefusal(
                MediaPackagePathBoundary.FaultCode(parentFault),
                "target",
                confirm.EnumeratedEntries);
        }

        RequireTargetParentDirectory(reopenedTarget!, "target", confirm.EnumeratedEntries);

        if (!await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            throw new InspectionRefusal("scope_changed", null, confirm.EnumeratedEntries);
        }

        return observed with { EnumeratedEntries = confirm.EnumeratedEntries };
    }

    private async ValueTask<PackageLoad> ReadDeclaredFilesAsync(
        MediaPackageInspectionScope scope,
        MediaPackageManifest manifest,
        MediaPackageInspectionLimits limits,
        MediaPackageIssueSink issues,
        MediaPackagePathBoundary stagingBoundary,
        string packageRoot,
        int enumeratedEntries,
        CancellationToken cancellationToken)
    {
        var observed = new List<ObservedFile>(manifest.Files.Count);
        long verifiedBytes = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false))
            {
                throw new InspectionRefusal("scope_changed", file.Path, enumeratedEntries);
            }

            var remaining = limits.MaximumReadByteCount - verifiedBytes;
            if (remaining <= 0 || file.SizeBytes > remaining)
            {
                throw new InspectionRefusal("budget_exceeded", file.Path, enumeratedEntries);
            }

            var facts = await ReadAndRecheckAsync(
                stagingBoundary,
                packageRoot,
                file,
                remaining,
                issues,
                enumeratedEntries,
                cancellationToken).ConfigureAwait(false);

            if (facts.Payload.Length != file.SizeBytes)
            {
                throw new InspectionRefusal("size_mismatch", file.Path, enumeratedEntries);
            }

            if (facts.Payload.Sha256 != file.Sha256)
            {
                throw new InspectionRefusal("hash_mismatch", file.Path, enumeratedEntries);
            }

            verifiedBytes += facts.Payload.Length;
            observed.Add(new ObservedFile(
                new MediaPackageObservedFile(file.Path, file.Kind, file.Cid, facts.Payload),
                facts.Stamp));
        }

        return new PackageLoad(observed, verifiedBytes, enumeratedEntries);
    }

    /// <summary>
    /// Resolves, observes, re-observes and hashes one declared file. The stamp taken before and after the
    /// read is what makes a quiet substitution during the read a verdict instead of a silent pass, and the
    /// declared length is compared with the remaining budget before a single byte is read. The stamp taken
    /// after the read is returned so the final re-check can compare the last-write stamp too.
    /// </summary>
    private async ValueTask<(PayloadFacts Payload, (long Length, DateTimeOffset ModifiedAt) Stamp)>
        ReadAndRecheckAsync(
            MediaPackagePathBoundary stagingBoundary,
            string packageRoot,
            MediaPackageFileEntry file,
            long remaining,
            MediaPackageIssueSink issues,
            int enumeratedEntries,
            CancellationToken cancellationToken)
    {
        if (!stagingBoundary.TryResolveChild(
                packageRoot,
                file.Path,
                out var absolutePath,
                out var resolveFault))
        {
            throw new InspectionRefusal(
                MediaPackagePathBoundary.FaultCode(resolveFault),
                file.Path,
                enumeratedEntries);
        }

        if (!stagingBoundary.TryObserve(absolutePath, out var length, out var observeFault))
        {
            throw new InspectionRefusal(
                MediaPackagePathBoundary.FaultReason(
                    MediaPackagePathFault.None,
                    observeFault,
                    "source_missing"),
                file.Path,
                enumeratedEntries);
        }

        if (!stagingBoundary.Contains(absolutePath, packageRoot))
        {
            throw new InspectionRefusal("unsafe_path", file.Path, enumeratedEntries);
        }

        var before = MediaPackagePathBoundary.Stamp(absolutePath);
        PayloadFacts facts;
        try
        {
            facts = await fileHasher
                .HashAsync(absolutePath, remaining, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MediaPackageBudgetExceededException)
        {
            // The real file outgrew the remaining read budget while it was being read: that is a budget
            // verdict, not an environment failure.
            throw new InspectionRefusal("budget_exceeded", file.Path, enumeratedEntries);
        }

        var after = MediaPackagePathBoundary.Stamp(absolutePath);
        if (after != before || length != facts.Length)
        {
            issues.Record("source_changed", file.Path);
            throw new InspectionRefusal(null, null, enumeratedEntries);
        }

        return (facts, after);
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

        // A package root that is missing and one that cannot be observed are different verdicts: the
        // second is an environment refusal, never an absence.
        faultCode = observeFault == MediaPackagePathFault.None
            ? "source_missing"
            : MediaPackagePathBoundary.FaultCode(observeFault);
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

    /// <summary>
    /// Decides whether the target volume can still hold the declared payload plus the mandatory headroom,
    /// and returns the one code that decision produced, or null when the capacity is sufficient. An
    /// unobservable volume is <c>target_unavailable</c>; only an observed value below the requirement is
    /// <c>insufficient_space</c>. Exactly one code is produced, so the same decision can never be reported
    /// twice with two different meanings.
    /// </summary>
    private async ValueTask<string?> TargetCapacityRefusalAsync(
        string targetDirectory,
        long declaredBytes,
        MediaPackageInspectionLimits limits,
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
            // An unobservable volume is refused; sufficiency is never assumed and insufficiency is never
            // inferred from a value that was never observed.
            return "target_unavailable";
        }

        var required = declaredBytes > long.MaxValue - limits.MinimumTargetHeadroomByteCount
            ? long.MaxValue
            : declaredBytes + limits.MinimumTargetHeadroomByteCount;
        return available.Value < required ? "insufficient_space" : null;
    }

    /// <summary>
    /// Re-observes every file whose bytes were already verified, so a change to an earlier file while a
    /// later one was being read becomes <c>source_changed</c> instead of a silent pass. The comparison uses
    /// the stored stamp, not the length alone: an equal-length rewrite is a change.
    /// </summary>
    private static void ReverifyObserved(
        MediaPackagePathBoundary stagingBoundary,
        string packageRoot,
        IReadOnlyList<ObservedFile> observed,
        MediaPackageIssueSink issues)
    {
        foreach (var file in observed)
        {
            if (!stagingBoundary.TryResolveChild(
                    packageRoot,
                    file.Entry.Path,
                    out var absolutePath,
                    out var resolveFault))
            {
                throw new InspectionRefusal(MediaPackagePathBoundary.FaultCode(resolveFault), file.Entry.Path);
            }

            if (!stagingBoundary.TryObserve(absolutePath, out var length, out var observeFault))
            {
                throw new InspectionRefusal(
                    MediaPackagePathBoundary.FaultReason(
                        MediaPackagePathFault.None,
                        observeFault,
                        "source_changed"),
                    file.Entry.Path);
            }

            if (length != file.Entry.Payload.Length
                || MediaPackagePathBoundary.Stamp(absolutePath) != file.Stamp)
            {
                issues.Record("source_changed", file.Entry.Path);
                throw new InspectionRefusal(null, null);
            }
        }
    }

    /// <summary>
    /// One verified file plus the stamp the inspector alone keeps for the final re-check. The stamp stays
    /// internal on purpose: the public <see cref="MediaPackageObservedFile"/> port describes what was
    /// verified, not how this implementation later re-proves it, so the port keeps its original shape.
    /// </summary>
    private sealed record ObservedFile(
        MediaPackageObservedFile Entry,
        (long Length, DateTimeOffset ModifiedAt) Stamp);

    /// <summary>
    /// The verified payload set of one accepted package, with the entries the walk really consumed.
    /// </summary>
    private sealed record PackageLoad(
        IReadOnlyList<ObservedFile> Files,
        long VerifiedBytes,
        int EnumeratedEntries);

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

    /// <summary>
    /// Order-independent comparison of two listings, so the walk's order is never part of the verdict.
    /// </summary>
    private static bool SameListing(
        MediaPackagePathBoundary.PackageListing left,
        MediaPackagePathBoundary.PackageListing right)
    {
        static string[] Sorted(IReadOnlyList<string> values) =>
            [.. values.OrderBy(value => value, StringComparer.Ordinal)];
        return Sorted(left.Files).SequenceEqual(Sorted(right.Files))
            && Sorted(left.Directories).SequenceEqual(Sorted(right.Directories));
    }

    /// <summary>
    /// A named package-level refusal. It exists so the ordered preflight can stop at the first verdict
    /// without losing which verdict it was, and without turning a verdict into a thrown framework
    /// exception. A null <see cref="Code"/> means the reason is already in the issue sink.
    /// </summary>
    private sealed class InspectionRefusal(
        string? code,
        string? location,
        int enumeratedEntries = 0) : Exception
    {
        public string? Code { get; } = code;

        public string? Location { get; } = location;

        public int EnumeratedEntries { get; } = enumeratedEntries;
    }
}
