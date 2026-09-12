using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AssetLibrary.Windows.Setup;

internal static class WindowsInteraction
{
    internal static void CheckPrerequisites()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new SetupException("unsupported_platform", "此安装包需要 Windows 11 x64。");
        }
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new SetupException("elevated_context", "请以普通用户双击运行安装器，不要选择“以管理员身份运行”。");
        }
        uint length = 0;
        if (GetCurrentPackageFullName(ref length, 0) != 15700)
        {
            throw new SetupException("packaged_context", "请解压安装包后，从 Windows 资源管理器双击安装器。");
        }
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? policies = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        if (policies?.GetValue("EnableLUA") is not int enabled || enabled != 1)
        {
            throw new SetupException("unsupported_uac", "当前系统 UAC 环境不支持本用户级安装，安装器未修改系统策略。");
        }
    }

    internal static async Task StopHostAsync(string directory, CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(Path.Combine(directory, "AssetLibrary.Host.exe"), "--shutdown-user-session")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            }
        };
        if (!process.Start()) { throw new SetupException("host_shutdown_failed", "无法请求后台会话退出。"); }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(7));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SetupException("host_shutdown_timeout", "后台会话未在时限内退出，请稍后重试。");
        }
        if (process.ExitCode != 0) { throw new SetupException("host_shutdown_failed", "后台会话拒绝退出，保留当前安装版本。"); }
    }

    internal static void StartApplications(string directory, bool settings)
    {
        Start(directory, "AssetLibrary.Host.exe", "--user-session");
        if (settings) { Start(directory, "AssetLibrary.Settings.exe", ""); }
    }

    private static void Start(string directory, string executable, string arguments)
    {
        using Process? process = Process.Start(new ProcessStartInfo(Path.Combine(directory, executable), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory
        });
        if (process is null) { throw new SetupException("application_start_failed", "安装已完成，但应用启动失败；可重新运行安装器。"); }
    }

    internal static bool Dialog(string instruction, string content, bool confirm)
    {
        int result = TaskDialog(0, 0, "资产库安装", instruction, content, confirm ? 9u : 1u, (nint)(-1), out int button);
        Marshal.ThrowExceptionForHR(result);
        return button == 1;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetCurrentPackageFullName(ref uint length, nint name);

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int TaskDialog(nint parent, nint instance, string title, string instruction,
        string content, uint buttons, nint icon, out int button);
}
