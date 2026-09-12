using System.ComponentModel;
using System.Text.Json;

namespace AssetLibrary.Windows.Setup;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        bool quiet = args.Contains("--quiet", StringComparer.Ordinal) || args.Contains("--sandbox", StringComparer.Ordinal);
        try
        {
            SetupOptions options = SetupOptions.Parse(args);
            if (args.Length == 0 && NeedsCleanup()) { options = options with { Command = "uninstall" }; }
            quiet = options.Quiet;
            if (options.Sandbox is null && options.Command != "status") { WindowsInteraction.CheckPrerequisites(); }
            if (!quiet && options.Command != "status" && !Confirm(options.Command)) { return 1223; }
            return await ExecuteAsync(options).ConfigureAwait(false);
        }
        catch (Exception error) when (error is SetupException or IOException or UnauthorizedAccessException or
                                     JsonException or OperationCanceledException or Win32Exception or System.Runtime.InteropServices.ExternalException)
        {
            string code = error is SetupException setup ? setup.Code : error.GetType().Name;
            string message = error is SetupException ? error.Message : "操作未完成，已保留原有数据。请检查包完整性、文件占用和当前用户权限后重试。";
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", code, message }, Product.Json));
            if (!quiet) { WindowsInteraction.Dialog("操作未完成", message + "\n错误代码：" + code, false); }
            return 1;
        }
    }

    private static bool NeedsCleanup()
    {
        if (!Directory.Exists(Product.DefaultRoot)) { return false; }
        Installer installer = new(Product.DefaultRoot, new WindowsRegistrationStore(), WindowsInteraction.StopHostAsync);
        return installer.Status().Status == "uninstalled_pending_cleanup";
    }

    private static async Task<int> ExecuteAsync(SetupOptions options)
    {
        string root = options.Sandbox is null ? Product.DefaultRoot : Path.Combine(options.Sandbox, "installation");
        if (options.Sandbox is not null) { Directory.CreateDirectory(options.Sandbox); }
        IRegistrationStore store = options.Sandbox is null ? new WindowsRegistrationStore() :
            new SandboxRegistrationStore(Path.Combine(options.Sandbox, "registry.json"));
        Installer installer = new(root, store, options.Sandbox is null ? WindowsInteraction.StopHostAsync :
            static (_, _) => Task.CompletedTask);
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(10));
        SetupReport report = options.Command switch
        {
            "install" => await installer.InstallAsync(options.Package, deadline.Token).ConfigureAwait(false),
            "uninstall" => await installer.UninstallAsync(deadline.Token).ConfigureAwait(false),
            _ => installer.Status()
        };
        List<string> warnings = [];
        if (options.Sandbox is null && options.Command != "status" && !ShellNotification.Notify())
        {
            warnings.Add("注册已完成，但资源管理器刷新未确认；请关闭资产库窗口后重新打开。");
        }
        if (options.Sandbox is null && options.Command == "install" && report.InstallDir is not null)
        {
            WindowsInteraction.StartApplications(report.InstallDir, !options.Quiet);
        }
        Console.Out.WriteLine(JsonSerializer.Serialize(new { report, warnings, unsignedPreview = true }, Product.Json));
        if (!options.Quiet)
        {
            string content = Describe(report) + (warnings.Count > 0 ? "\n" + string.Join("\n", warnings) : "");
            WindowsInteraction.Dialog("资产库", content, false);
        }
        return report.PendingCleanup.Length > 0 ? 3010 : 0;
    }

    private static bool Confirm(string command) => WindowsInteraction.Dialog(
        command == "uninstall" ? "卸载资产库 Explorer 浏览" : "安装资产库 Explorer 浏览",
        command == "uninstall" ? "将移除当前用户的资源管理器入口和应用。连接配置会保留。被资源管理器占用的组件将明确记录为待清理。" :
        "安装 Windows 11 x64 原生资源管理器只读浏览版。此预览包尚未签名。安装后打开连接设置；资产原文件不会被修改。", true);

    private static string Describe(SetupReport report) => report.Status switch
    {
        "installed" => "安装完成。请在连接设置中登录，然后从资源管理器左侧的“资产库”进入。",
        "uninstalled" => "已卸载应用与当前用户注册。连接配置已保留。",
        "uninstalled_pending_cleanup" => "已移除入口。以下版本仍有占用或无法安全删除的文件：\n" +
            string.Join("\n", report.PendingCleanup) + "\n请关闭资产库窗口后重新运行解压包中的安装器并卸载；安装器不会结束资源管理器。",
        "not_installed" => "当前用户未注册资产库。",
        _ => "当前进程注册视图：" + report.Version + "\n" + report.InstallDir + "\n此状态不代替实际资源管理器可见性验证。"
    };
}
