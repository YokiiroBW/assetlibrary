using System.Net.Sockets;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal sealed class UnixSocketImageDecoder : IImageDecoder, IDisposable
{
    internal const string SocketPath = "/run/assetlibrary-image/decoder.sock";
    private readonly string path;
    private readonly Func<Socket, bool> verifyPeer;
    private readonly SemaphoreSlim capacity = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object lifecycle = new();
    private int callers;
    private bool disposed;
    private bool cancellationFinished;

    public UnixSocketImageDecoder() : this(SocketPath, UnixSocketImagePeer.IsRoot) { }

    // Internal test seam only. Runtime configuration cannot replace the fixed
    // endpoint or peer policy with an arbitrary path/delegate.
    internal UnixSocketImageDecoder(string path, Func<Socket, bool> verifyPeer)
    {
        this.path = path;
        this.verifyPeer = verifyPeer;
    }

    public async ValueTask<byte[]> DecodeAsync(IImageSourceLease source, ImagePreviewVariant variant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length <= 0) throw new ImagePreviewException(ImagePreviewFailure.Invalid);
        if (source.Length > ImageWorkerProtocol.MaximumSourceBytes)
            throw new ImagePreviewException(ImagePreviewFailure.LimitExceeded);
        _ = ImageWorkerProtocol.MaximumEdge((int)variant);
        var stopping = Enter();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var admitted = false;
            try
            {
                // Waiting behind the other application request consumes the
                // same eight seconds, not a fresh budget after admission.
                await capacity.WaitAsync(deadline.Token).ConfigureAwait(false);
                admitted = true;
                return await UnixSocketImageTransport.ExchangeAsync(path, verifyPeer, source, variant, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception) when (stopping.IsCancellationRequested)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            }
            catch (Exception) when (deadline.IsCancellationRequested)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Timeout);
            }
            catch (InvalidDataException)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Invalid);
            }
            catch (Exception failure) when (failure is IOException or SocketException or ObjectDisposedException)
            {
                throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            }
            finally
            {
                if (admitted) capacity.Release();
            }
        }
        finally { Leave(); }
    }

    private CancellationToken Enter()
    {
        lock (lifecycle)
        {
            if (disposed) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
            callers++;
            return shutdown.Token;
        }
    }

    private void Leave()
    {
        lock (lifecycle)
        {
            callers--;
            if (disposed && cancellationFinished && callers == 0) ReleaseSynchronization();
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
        }
        // Cancel closes active sockets and wakes semaphore waiters; never join
        // an exchange here. Its finally owns Release until the last caller exits.
        try { shutdown.Cancel(); }
        finally
        {
            lock (lifecycle)
            {
                cancellationFinished = true;
                if (callers == 0) ReleaseSynchronization();
            }
        }
    }

    private void ReleaseSynchronization()
    {
        capacity.Dispose();
        shutdown.Dispose();
    }
}
