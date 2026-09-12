using AssetLibrary.Windows.Session;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssetLibrary.Windows.Settings;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool initialized;
    private bool working;
    private bool disposed;
    private bool readingStatus;
    private ConnectionStatus? lastStatus;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(650, 790));
        Activated += OnActivated;
        statusTimer.Tick += ReadStatus;
        statusTimer.Start();
        Closed += (_, _) => Dispose();
    }

    private async void ReadStatus(object? sender, object args)
    {
        if (!initialized || working || readingStatus || disposed) { return; }
        readingStatus = true;
        try
        {
            var status = (await HostConnection.StatusAsync(lifetime.Token)).Status;
            if (status != lastStatus) { Apply(status); }
        }
        catch (Exception error) when (HostConnection.IsConnectionFailure(error)) { ShowFailure("后台连接不可用。请重新打开设置检查安装状态。"); }
        finally { readingStatus = false; }
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (initialized || args.WindowActivationState == WindowActivationState.Deactivated) { return; }
        initialized = true;
        try
        {
            var response = await HostConnection.InitializeAsync(lifetime.Token);
            Apply(response.Status, populate: true);
        }
        catch (Exception error) when (HostConnection.IsConnectionFailure(error)) { ShowFailure("后台连接不可用，请确认安装完整后重新打开设置。"); }
    }

    private async void ConnectClick(object sender, RoutedEventArgs args)
    {
        if (working) { return; }
        var password = PasswordBox.Password;
        PasswordBox.Password = "";
        SetWorking(true);
        try
        {
            var response = await HostConnection.ConnectAsync(OriginBox.Text, CertificateBox.Text, AccountBox.Text, password, RememberBox.IsChecked == true, lifetime.Token);
            Apply(response.Status, updateRemember: true);
            if (!response.Ok) { ShowFailure(HostConnection.ErrorMessage(response.ErrorCode)); }
        }
        catch (InvalidDataException) { ShowFailure("请检查 HTTPS 地址、账号和密码是否填写完整。"); }
        catch (Exception error) when (HostConnection.IsConnectionFailure(error)) { ShowFailure("连接未完成。请检查后台状态后重试。"); }
        finally { SetWorking(false); }
    }

    private async void DisconnectClick(object sender, RoutedEventArgs args)
    {
        DisconnectButton.IsEnabled = false;
        PasswordBox.Password = "";
        try
        {
            var response = await HostConnection.DisconnectAsync(lifetime.Token);
            Apply(response.Status, updateRemember: true);
            if (!response.Ok) { ShowFailure(HostConnection.ErrorMessage(response.ErrorCode)); }
        }
        catch (Exception error) when (HostConnection.IsConnectionFailure(error)) { ShowFailure("无法确认退出，请重新打开设置检查连接状态。"); }
        finally { DisconnectButton.IsEnabled = true; }
    }

    private void Apply(ConnectionStatus status, bool populate = false, bool updateRemember = false)
    {
        if (disposed) { return; }
        lastStatus = status;
        if (populate || string.IsNullOrEmpty(OriginBox.Text) && string.IsNullOrEmpty(AccountBox.Text))
        {
            OriginBox.Text = status.Origin ?? "";
            CertificateBox.Text = status.CertificateSha256 ?? "";
            AccountBox.Text = status.AccountName ?? "";
        }
        if (populate || updateRemember) { RememberBox.IsChecked = status.RememberLogin; }
        StatusBar.Severity = HostConnection.IsConnected(status) ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        StatusBar.Title = HostConnection.StatusTitle(status);
        StatusBar.Message = HostConnection.IsConnected(status)
            ? "请从资源管理器的“资产库”浏览。关闭本窗口不会退出登录。"
            : "填写或检查下方配置，然后连接并登录。";
    }

    private void SetWorking(bool value)
    {
        if (disposed) { return; }
        working = value;
        ConnectButton.IsEnabled = !value;
        BusyRing.IsActive = value;
        if (value) { StatusBar.Title = "正在连接"; StatusBar.Message = "正在验证服务器与账号。可点击退出登录取消。"; }
    }
    private void ShowFailure(string message)
    {
        if (disposed) { return; }
        StatusBar.Severity = InfoBarSeverity.Error; StatusBar.Title = "连接需要处理"; StatusBar.Message = message;
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        statusTimer.Stop();
        statusTimer.Tick -= ReadStatus;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
