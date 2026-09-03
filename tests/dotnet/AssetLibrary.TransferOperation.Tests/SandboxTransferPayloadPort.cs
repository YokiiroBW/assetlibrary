using AssetLibrary.Modules.TransferSync.Application;
using AssetLibrary.Modules.TransferSync.Contracts;

namespace AssetLibrary.TransferOperation.Tests;

internal sealed class SandboxTransferPayloadPort(
    SandboxFixture fixture,
    SandboxStreamCopy streamCopy,
    SandboxFileHash fileHash) : ITransferPayloadPort
{
    public long? AvailableBytesOverride { get; set; }

    public int LastChunkCount { get; private set; }

    public int LastMaximumChunk { get; private set; }

    public async ValueTask<TransferPortReceipt> TransferAsync(
        TransferRequest request,
        TransferExecutionRight executionRight,
        CancellationToken cancellationToken)
    {
        var source = fixture.Resolve(request.Source);
        var target = fixture.Resolve(request.Target);
        fixture.Boundary.Revalidate(source);
        fixture.Boundary.Revalidate(target);
        if (!File.Exists(source))
        {
            return Failure(request, TransferPortOutcome.SourceChanged);
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return Failure(request, TransferPortOutcome.TargetExists);
        }

        if (AvailableBytes() < request.ExpectedSource.Length)
        {
            return Failure(request, TransferPortOutcome.InsufficientSpace);
        }

        var observed = await fileHash.ComputeAsync(source, cancellationToken).ConfigureAwait(false);
        if (observed != request.ExpectedSource)
        {
            return Failure(request, TransferPortOutcome.SourceChanged);
        }

        var stage = fixture.Boundary.ResolveInternal(
            "stage",
            request.SessionId.Value.ToString("N"),
            "payload.partial");
        SandboxFilePrimitives.EnsureParent(fixture.Boundary, stage);
        SandboxCopyResult copied;
        try
        {
            copied = await streamCopy.CopyAsync(source, stage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SandboxFilePrimitives.DeleteEvidenceFile(fixture.Boundary, stage);
            throw;
        }

        LastChunkCount = copied.Chunks;
        LastMaximumChunk = copied.MaximumChunk;
        if (copied.Facts != request.ExpectedSource)
        {
            SandboxFilePrimitives.DeleteEvidenceFile(fixture.Boundary, stage);
            return Failure(request, TransferPortOutcome.SourceChanged, copied.Facts.Length);
        }

        var currentSource = await fileHash.ComputeAsync(source, cancellationToken).ConfigureAwait(false);
        if (currentSource != request.ExpectedSource)
        {
            SandboxFilePrimitives.DeleteEvidenceFile(fixture.Boundary, stage);
            return Failure(request, TransferPortOutcome.SourceChanged, copied.Facts.Length);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!SandboxFilePrimitives.MoveNoReplace(fixture.Boundary, stage, target))
        {
            return Failure(request, TransferPortOutcome.TargetExists, copied.Facts.Length);
        }

        using var finalization = SandboxFinalization.Create(
            request.Deadline,
            executionRight.ExpiresAt);
        try
        {
            var reopened = await fileHash.ComputeAsync(
                target,
                finalization.Token).ConfigureAwait(false);
            return reopened == request.ExpectedSource
                ? new TransferPortReceipt(
                    request.SessionId,
                    TransferPortOutcome.Completed,
                    copied.Facts.Length,
                    reopened)
                : Failure(request, TransferPortOutcome.HashMismatch, copied.Facts.Length);
        }
        catch (OperationCanceledException) when (finalization.IsCancellationRequested)
        {
            return Failure(
                request,
                TransferPortOutcome.ManualReviewRequired,
                copied.Facts.Length);
        }
    }

    private long AvailableBytes()
    {
        if (AvailableBytesOverride.HasValue)
        {
            return AvailableBytesOverride.Value;
        }

        var root = Path.GetPathRoot(fixture.Root)
            ?? throw new InvalidOperationException("The sandbox volume root is unavailable.");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    private static TransferPortReceipt Failure(
        TransferRequest request,
        TransferPortOutcome outcome,
        long bytes = 0) =>
        new(request.SessionId, outcome, bytes, null);
}
