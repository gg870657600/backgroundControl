using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using backgroundControl.Tools;

// 解决 WPF/WinForms 命名冲突（项目启用了 UseWindowsForms）
using Clipboard   = System.Windows.Clipboard;
using MessageBox  = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace backgroundControl.Views;

public partial class FtpServerControl : System.Windows.Controls.UserControl
{
    private readonly ToolsConfig _cfg;
    private FtpServerHost? _server;

    public event Action<bool>?   OnStateChanged;
    public event Action<string>? OnLogAppended;

    public void StopService()
    {
        try { _server?.Stop(); }
        catch { }
        _server?.Dispose();
        _server = null;
    }

    public FtpServerControl()
    {
        InitializeComponent();
        _cfg = ToolsConfig.Load();

        TxtPort.Text       = _cfg.Ftp.Port.ToString();
        TxtRoot.Text       = _cfg.Ftp.RootDir;
        TxtPasvStart.Text  = _cfg.Ftp.PassiveStart.ToString();
        TxtPasvEnd.Text    = _cfg.Ftp.PassiveEnd.ToString();
        ChkAnonymous.IsChecked = _cfg.Ftp.AllowAnonymous;

        AppendLog("就绪。请配置参数后点击 启动。");
    }

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description       = "选择 FTP 服务根目录",
            UseDescriptionForTitle = true,
            SelectedPath      = TxtRoot.Text,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtRoot.Text = dlg.SelectedPath;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtPort.Text, out var port) || port < 1 || port > 65535)
        { MessageBox.Show("端口无效", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(TxtPasvStart.Text, out var pasvStart) || pasvStart < 1 || pasvStart > 65535)
        { MessageBox.Show("被动端口起无效", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(TxtPasvEnd.Text, out var pasvEnd) || pasvEnd < pasvStart || pasvEnd > 65535)
        { MessageBox.Show("被动端口止必须 ≥ 起，且 ≤ 65535", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!System.IO.Directory.Exists(TxtRoot.Text))
        { MessageBox.Show($"根目录不存在: {TxtRoot.Text}", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        _cfg.Ftp.Port           = port;
        _cfg.Ftp.RootDir        = TxtRoot.Text;
        _cfg.Ftp.PassiveStart   = pasvStart;
        _cfg.Ftp.PassiveEnd     = pasvEnd;
        _cfg.Ftp.AllowAnonymous = ChkAnonymous.IsChecked == true;
        try { _cfg.Save(); } catch { }

        try
        {
            _server = new FtpServerHost(_cfg.Ftp);
            _server.OnLog   += msg => Dispatcher.Invoke(() => AppendLog(msg));
            _server.OnState += running => Dispatcher.Invoke(() => OnRunningChanged(running));
            _server.Start();

            TxtUrl.Text = $"ftp://{GetLocalIP()}:{port}";
            TxtUrl.Foreground = System.Windows.Media.Brushes.Green;
            OnStateChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
            _server = null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try { _server?.Stop(); }
        catch (Exception ex) { AppendLog($"停止失败: {ex.Message}"); }
        finally
        {
            _server?.Dispose();
            _server = null;
        }
    }

    private void OnRunningChanged(bool running)
    {
        TxtPort.IsEnabled      = !running;
        TxtRoot.IsEnabled      = !running;
        TxtPasvStart.IsEnabled = !running;
        TxtPasvEnd.IsEnabled   = !running;
        ChkAnonymous.IsEnabled = !running;
        BtnStart.IsEnabled     = !running;
        BtnStop.IsEnabled      = running;
        BtnStart.Visibility    = running ? Visibility.Collapsed : Visibility.Visible;
        BtnStop.Visibility     = running ? Visibility.Visible   : Visibility.Collapsed;

        if (!running)
        {
            TxtUrl.Text = "(未启动)";
            TxtUrl.Foreground = System.Windows.Media.Brushes.Gray;
            OnStateChanged?.Invoke(false);
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(TxtUrl.Text) && TxtUrl.Text != "(未启动)")
        {
            try { Clipboard.SetText(TxtUrl.Text); AppendLog($"已复制: {TxtUrl.Text}"); } catch { }
        }
    }

    private void AppendLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        TxtLog.AppendText(line);
        LogScroll.ScrollToEnd();
        OnLogAppended?.Invoke(line);
    }

    private static string GetLocalIP()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                ?.ToString() ?? "localhost";
        }
        catch { return "localhost"; }
    }
}
