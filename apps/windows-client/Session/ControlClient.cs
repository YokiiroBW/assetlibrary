using System.Runtime.Versioning;

namespace AssetLibrary.Windows.Session;

[SupportedOSPlatform("windows")]
public static class ControlClient
{
    public static async Task<ControlResponse> SendAsync(ControlRequest request, CancellationToken token, string? endpoint = null)
    {
        ControlProtocol.Validate(request);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ControlProtocol.ExchangeTimeout);
        await using var pipe = await LocalPipe.OpenClientAsync(endpoint ?? LocalPipe.ControlEndpoint, deadline.Token).ConfigureAwait(false);
        await ControlProtocol.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
        var response = await ControlProtocol.ReadAsync<ControlResponse>(pipe, deadline.Token).ConfigureAwait(false);
        if (response.Version != 1 || response.RequestId != request.RequestId || response.Status is null
            || response.Ok != (response.ErrorCode is null))
        { throw new InvalidDataException("Invalid Host response."); }
        return response;
    }
}
