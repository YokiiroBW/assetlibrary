using System.Net.Sockets;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Application;
using AssetLibrary.Modules.PreviewProvider.Contracts;

namespace AssetLibrary.Modules.PreviewProvider.Infrastructure;

internal static class UnixSocketImageTransport
{
    public static async Task<byte[]> ExchangeAsync(string path, Func<Socket, bool> verifyPeer,
        IImageSourceLease source, ImagePreviewVariant variant, CancellationToken token)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var disconnect = token.Register(static value => ((Socket)value!).Dispose(), socket);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token).ConfigureAwait(false);
        if (!verifyPeer(socket)) throw new ImagePreviewException(ImagePreviewFailure.Unavailable);
        using var stream = new NetworkStream(socket, ownsSocket: false);
        var ready = await ImageWorkerTransport.ReadHeaderAsync(stream, token).ConfigureAwait(false);
        RequireEmpty(ready);
        if (ready.Status != (int)ImageWorkerStatus.Ready) RequireFailure(ready);
        await stream.WriteAsync(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Request, (int)variant,
            checked((int)source.Length)), token).ConfigureAwait(false);
        // ProcessImageSourceLease already streams exactly Length and verifies
        // its hash. Keep both socket directions open for disconnect monitoring.
        await source.CopyToAsync(stream, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        var response = await ImageWorkerTransport.ReadHeaderAsync(stream, token).ConfigureAwait(false);
        if (response.Status != (int)ImageWorkerStatus.Success)
        {
            RequireEmpty(response);
            RequireFailure(response);
        }
        if (response.Length is < 33 || response.Length > ImageWorkerProtocol.MaximumOutput((int)variant))
            throw new ImagePreviewException(ImagePreviewFailure.LimitExceeded);
        if (response.Profile != (int)variant) throw new ImagePreviewException(ImagePreviewFailure.Invalid);
        var png = new byte[response.Length];
        await stream.ReadExactlyAsync(png, token).ConfigureAwait(false);
        ImageWorkerTransport.ValidatePng(png, response, variant);
        token.ThrowIfCancellationRequested();
        if (socket.Available != 0) throw new ImagePreviewException(ImagePreviewFailure.Invalid);
        // One framed result is enough. Do not wait for EOF or half-close: the
        // supervisor has already reaped its decoder before publishing success.
        return png;
    }

    private static void RequireEmpty(ImageWorkerHeader frame)
    {
        if (frame.Profile != 0 || frame.Length != 0 || frame.Width != 0 || frame.Height != 0)
            throw new ImagePreviewException(ImagePreviewFailure.Invalid);
    }

    private static void RequireFailure(ImageWorkerHeader frame)
    {
        if (frame.Status is < (int)ImageWorkerStatus.Invalid or > (int)ImageWorkerStatus.Unavailable)
            throw new ImagePreviewException(ImagePreviewFailure.Invalid);
        ImageWorkerTransport.RequireStatus(frame, ImageWorkerStatus.Success);
    }
}
