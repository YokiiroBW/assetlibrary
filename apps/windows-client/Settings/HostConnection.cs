using System.Diagnostics;
using AssetLibrary.Windows.Session;

namespace AssetLibrary.Windows.Settings;

internal static class HostConnection
{
    internal static bool IsConnected(ConnectionStatus status) => status.State == ConnectionState.Connected;
    internal static string StatusTitle(ConnectionStatus status) => status.State switch
    {
        ConnectionState.Connected => "已连接 · " + status.DisplayName,
        ConnectionState.Connecting => "正在连接",
        ConnectionState.AccessDenied => "需要重新登录",
        ConnectionState.Unavailable => "连接不可用",
        ConnectionState.Unconfigured => "尚未配置连接",
        _ => "已退出登录",
    };
    internal static Task<ControlResponse> ConnectAsync(string origin, string? certificate, string account, string password, bool remember, CancellationToken token)
    {
        var connection = new ConnectionInput(origin.Trim(), string.IsNullOrWhiteSpace(certificate) ? null : certificate.Trim(), account.Trim(), password, remember);
        return ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Connect, connection), token);
    }
    internal static Task<ControlResponse> DisconnectAsync(CancellationToken token) =>
        ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Disconnect), token);
    internal static Task<ControlResponse> StatusAsync(CancellationToken token) =>
        ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Status), token);
    internal static bool IsConnectionFailure(Exception error) => error is IOException or InvalidDataException or OperationCanceledException
        or System.ComponentModel.Win32Exception or System.Text.Json.JsonException or UnauthorizedAccessException;

    internal static async Task<ControlResponse> InitializeAsync(CancellationToken token)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
        probe.CancelAfter(TimeSpan.FromMilliseconds(350));
        try { return await ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Status), probe.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AssetLibrary.Host.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--user-session");
            using var process = Process.Start(info) ?? throw new IOException("Host could not start.");
        }
        return await ControlClient.SendAsync(new ControlRequest(1, Guid.NewGuid(), ControlOperation.Status), token);
    }

    internal static string ErrorMessage(ControlError? error) => error switch
    {
        ControlError.AccessDenied => "账号密码无效、权限被拒绝或登录已过期。旧会话已清除，请重新登录。",
        ControlError.InvalidRequest => "请检查 HTTPS 地址、证书指纹、账号和密码。地址不能包含路径。",
        ControlError.Busy => "后台正在处理连接变更。请稍后重试，也可点击退出登录取消。",
        ControlError.Cancelled => "连接已取消。",
        ControlError.StorageError => "无法安全保存或读取本用户配置。请检查用户目录权限和可用空间。",
        _ => "无法连接服务器。请检查网络、HTTPS 地址和证书信任后重试。",
    };
}
