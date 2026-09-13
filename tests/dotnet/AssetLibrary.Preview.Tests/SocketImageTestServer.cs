using System.Buffers.Binary;
using System.Net.Sockets;
using AssetLibrary.ImagePreview.Protocol;
using AssetLibrary.Modules.PreviewProvider.Application;

namespace AssetLibrary.Preview.Tests;

internal sealed class SocketImageTestServer : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "al-socket-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource stopping = new(TimeSpan.FromSeconds(20));
    private readonly Task serving;

    public SocketImageTestServer(Func<Socket, int, CancellationToken, Task> handler, int connections = 1)
    {
        Directory.CreateDirectory(directory);
        PathName = Path.Combine(directory, "image.sock");
        listener.Bind(new UnixDomainSocketEndPoint(PathName));
        listener.Listen(2);
        serving = ServeAsync(handler, connections);
    }

    public string PathName { get; }

    public static byte[] Png => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "skia-rgba8-sbit.png"));

    public static async Task<ImageWorkerHeader> RequestAsync(Socket socket, CancellationToken token)
    {
        using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(ImageWorkerProtocol.Header((int)ImageWorkerStatus.Ready, 0, 0), token);
        var header = new byte[ImageWorkerProtocol.HeaderBytes];
        await stream.ReadExactlyAsync(header, token);
        var request = ImageWorkerProtocol.ReadHeader(header);
        Assert.AreEqual((int)ImageWorkerStatus.Request, request.Status);
        Assert.AreEqual(4, request.Length);
        Assert.AreEqual(0, request.Width);
        Assert.AreEqual(0, request.Height);
        var body = new byte[request.Length];
        await stream.ReadExactlyAsync(body, token);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, body);
        return request;
    }

    public static byte[] Response(int profile, bool trailing = false)
    {
        var png = Png;
        var header = ImageWorkerProtocol.Header((int)ImageWorkerStatus.Success, profile, png.Length,
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        return [.. header, .. png, .. (trailing ? new byte[] { 99 } : Array.Empty<byte>())];
    }

    public static async Task RespondAndObserveCloseAsync(Socket socket, int profile, CancellationToken token)
    {
        using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(Response(profile), token);
        Assert.AreEqual(0, await stream.ReadAsync(new byte[1], token), "Core closes after the complete frame, without waiting for EOF.");
    }

    private async Task ServeAsync(Func<Socket, int, CancellationToken, Task> handler, int connections)
    {
        try
        {
            for (var index = 0; index < connections; index++)
            {
                using var socket = await listener.AcceptAsync(stopping.Token);
                await handler(socket, index, stopping.Token);
            }
        }
        catch (Exception failure) when (stopping.IsCancellationRequested
            && failure is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            return;
        }
    }

    public Task CompleteAsync() => serving.WaitAsync(TimeSpan.FromSeconds(12));

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Dispose();
        try { await serving; }
        finally
        {
            stopping.Dispose();
            File.Delete(PathName);
            Directory.Delete(directory);
        }
    }
}

internal sealed class SocketImageSource : IImageSourceLease
{
    public long Length { get; init; } = 4;
    public string ContentHash => new('a', 64);
    public int Copies { get; private set; }
    public bool Changed { get; init; }
    public async ValueTask CopyToAsync(Stream output, CancellationToken cancellationToken)
    {
        Copies++;
        await output.WriteAsync(new byte[] { 1, 2, 3, 4 }, cancellationToken);
        if (Changed) throw new AssetLibrary.Modules.PreviewProvider.Contracts.ImagePreviewException(
            AssetLibrary.Modules.PreviewProvider.Contracts.ImagePreviewFailure.SourceChanged);
    }
    public ValueTask VerifyAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
