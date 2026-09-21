using AssetLibrary.Modules.LibraryStorage.Contracts;
using AssetLibrary.Modules.OperationTrash.Contracts;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.Modules.OperationTrash.Application;

/// <summary>
/// Single read-only preflight entry point for a prepared inbound media package. It orchestrates
/// validation, authorization, budget-limited real file inspection and report assembly; it never
/// prepares an operation plan, never creates a directory and never grants a file operation.
/// </summary>
public sealed class MediaPackageInspectionService(
    IMediaPackageManifestReader manifestReader,
    IMediaPackageInspectionScopeQuery scopeQuery,
    IMediaPackageInspectionPort inspectionPort,
    TimeProvider timeProvider,
    MediaPackageInspectionLimits limits) : IDisposable
{
    private readonly SemaphoreSlim inspectionGate = new(
        MediaPackageInspectionLimits.MaximumConcurrentInspections,
        MediaPackageInspectionLimits.MaximumConcurrentInspections);

    /// <summary>
    /// Releases the single-instance gate. Disposal is idempotent and never touches a directory or a file.
    /// </summary>
    public void Dispose() => inspectionGate.Dispose();

    public async ValueTask<MediaPackagePreflightReport> InspectAsync(
        MediaPackagePreflightRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Caller);
        using var budget = new CancellationTokenSource(
            limits.MaximumInspectionDuration,
            timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            budget.Token);
        try
        {
            return await InspectCoreAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The budget elapsed. Cancellation requested by the caller is not a rejection and
            // propagates instead.
            return Rejected(request.ExpectedManifestDigest, null, "timeout");
        }
    }

    private async ValueTask<MediaPackagePreflightReport> InspectCoreAsync(
        MediaPackagePreflightRequest request,
        CancellationToken cancellationToken)
    {
        var bytes = request.ManifestBytes
            ?? throw new ArgumentException("Manifest bytes are required.", nameof(request));
        if (bytes.Length == 0 || bytes.Length > limits.MaximumManifestByteCount)
        {
            return Rejected(request.ExpectedManifestDigest, null, "budget_exceeded");
        }

        // Shape and cross-field checks complete before any root or file is touched.
        var read = manifestReader.Read(bytes, request.ExpectedManifestDigest);
        if (!read.Succeeded)
        {
            return Rejected(
                read.ManifestDigest,
                read.Manifest,
                read.FailureCode ?? "invalid_manifest",
                issues: read.Issues,
                issuesTruncated: read.IssuesTruncated);
        }

        var manifest = read.Manifest!;
        if (manifest.Files.Count > limits.MaximumFileCount)
        {
            return Rejected(read.ManifestDigest, manifest, "budget_exceeded");
        }

        if (!await inspectionGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Rejected(read.ManifestDigest, manifest, "busy");
        }

        try
        {
            var scope = await scopeQuery.ResolveAsync(
                request.Caller,
                manifest.StagingRef,
                manifest.LibraryId,
                cancellationToken).ConfigureAwait(false);
            if (scope is null)
            {
                return Rejected(read.ManifestDigest, manifest, "unauthorized");
            }

            ValidateScope(scope);

            // The frozen classification keeps three different causes apart. An offline or unknown storage
            // root is an availability refusal (`target_unavailable`); an expired, revoked or changed
            // permission is a scope change (`scope_changed`); a missing source is `source_missing`. The
            // availability decision and the currency decision therefore stay separate, here and at the end.
            if (!IsAvailable(scope))
            {
                return Rejected(read.ManifestDigest, manifest, "target_unavailable", scope);
            }

            if (!IsCurrent(scope)
                || !await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false))
            {
                return Rejected(read.ManifestDigest, manifest, "scope_changed", scope);
            }

            var startedAt = timeProvider.GetUtcNow();
            var outcome = await inspectionPort.InspectAsync(
                scope,
                manifest,
                limits,
                cancellationToken).ConfigureAwait(false);
            var completedAt = timeProvider.GetUtcNow();
            ValidateOutcomeScope(outcome, scope);
            if (outcome.Issues.IsEmpty && !IsAvailable(scope))
            {
                outcome.Issues.Record("target_unavailable");
            }
            else if (outcome.Issues.IsEmpty
                && (!IsCurrent(scope)
                    || !await scopeQuery.IsCurrentAsync(scope, cancellationToken).ConfigureAwait(false)))
            {
                outcome.Issues.Record("scope_changed");
            }

            if (completedAt - startedAt > limits.MaximumInspectionDuration)
            {
                outcome.Issues.Record("timeout");
            }

            return Build(read.ManifestDigest, manifest, scope, completedAt, outcome);
        }
        finally
        {
            inspectionGate.Release();
        }
    }

    /// <summary>
    /// True while the granted storage root is usable at all. Only the storage availability is decided here:
    /// an offline or otherwise unavailable root is refused as <c>target_unavailable</c>. An expired, revoked
    /// or changed permission is decided by <see cref="IsCurrent"/> and
    /// <see cref="IMediaPackageInspectionScopeQuery.IsCurrentAsync"/>, so the two verdicts never collapse
    /// into one code.
    /// </summary>
    private static bool IsAvailable(MediaPackageInspectionScope scope) =>
        scope.Availability == StorageAvailability.Online;

    /// <summary>
    /// True while the granted permission is still in force. A lapsed lease is a scope change, not a storage
    /// availability refusal: the storage may be perfectly reachable while the caller's right to read it has
    /// expired, and the frozen vocabulary names those two conditions differently.
    /// </summary>
    private bool IsCurrent(MediaPackageInspectionScope scope) =>
        scope.ExpiresAt > timeProvider.GetUtcNow();

    private static void ValidateOutcomeScope(
        MediaPackageInspectionOutcome outcome,
        MediaPackageInspectionScope scope)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(outcome.Files);
        ArgumentNullException.ThrowIfNull(outcome.Issues);
        if (outcome.Scope != scope)
        {
            throw new InvalidOperationException(
                "The inspection port returned facts for another scope.");
        }
    }

    private static MediaPackagePreflightReport Build(
        Sha256Digest manifestDigest,
        MediaPackageManifest manifest,
        MediaPackageInspectionScope scope,
        DateTimeOffset observedAt,
        MediaPackageInspectionOutcome outcome)
    {
        var inspected = outcome.Issues.IsEmpty && outcome.Files.Count > 0;
        return new MediaPackagePreflightReport(
            inspected
                ? MediaPackageInspectionStatus.Inspected
                : MediaPackageInspectionStatus.Rejected,
            GrantsFileOperation: false,
            manifest.PackageId,
            manifestDigest,
            scope.Revision,
            observedAt,
            inspected ? outcome.Files.Count : 0,
            inspected ? outcome.VerifiedBytes : 0,
            [.. outcome.Issues.Issues],
            outcome.Issues.IsTruncated);
    }

    private MediaPackagePreflightReport Rejected(
        Sha256Digest manifestDigest,
        MediaPackageManifest? manifest,
        string code,
        MediaPackageInspectionScope? scope = null,
        IReadOnlyList<MediaPackageIssue>? issues = null,
        bool issuesTruncated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        IReadOnlyList<MediaPackageIssue> recorded = issues is { Count: > 0 }
            ? issues
            : [new MediaPackageIssue(code, null)];
        return new MediaPackagePreflightReport(
            MediaPackageInspectionStatus.Rejected,
            GrantsFileOperation: false,
            manifest?.PackageId,
            manifestDigest,
            scope?.Revision,
            timeProvider.GetUtcNow(),
            0,
            0,
            recorded,
            issuesTruncated);
    }

    private static void ValidateScope(MediaPackageInspectionScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.Revision)
            || scope.ExpiresAt == default
            || string.IsNullOrWhiteSpace(scope.StagingRoot.Value)
            || string.IsNullOrWhiteSpace(scope.TargetLibraryRoot.Value))
        {
            throw new InvalidOperationException(
                "The inspection scope port returned an incomplete scope.");
        }
    }
}
