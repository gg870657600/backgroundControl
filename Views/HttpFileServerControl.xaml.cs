using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using backgroundControl.Tools;

// 解决 WPF/WinForms 命名冲突（项目启用了 UseWindowsForms）
using Clipboard   = System.Windows.Clipboard;
using MessageBox  = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace backgroundControl.Views;

/// <summary>
/// HTTP 文件服务 Tab 的 UI 逻辑。
/// 设计：单实例服务 + 配置加载/保存到 ToolsConfig.json。
/// </summary>
public partial class HttpFileServerControl : System.Windows.Controls.UserControl
{
    private HttpFileServer? _server;
    private readonly ToolsConfig _cfg;

    public event Action<bool>?   OnStateChanged;     // 透传到 ToolsWindow 底部状态栏
    public event Action<string>? OnLogAppended;      // 透传到统一日志

    public void StopService()
    {
        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }

    public HttpFileServerControl()
    {
        InitializeComponent();
        _cfg = ToolsConfig.Load();
        TxtPort.Text      = _cfg.Http.Port.ToString();
        TxtRoot.Text      = _cfg.Http.RootDir;

        AppendLog("就绪。请配置端口和根目录，然后点击 启动。");
    }

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        // WPF 没有内置 OpenFolderDialog，用 WinForms 的 FolderBrowserDialog
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description       = "选择 HTTP 服务根目录",
            UseDescriptionForTitle = true,
            SelectedPath      = TxtRoot.Text,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtRoot.Text = dlg.SelectedPath;
    }

    private void AppendLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        TxtLog.AppendText(line);
        LogScroll.ScrollToEnd();
        OnLogAppended?.Invoke(line);
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(TxtUrl.Text) && TxtUrl.Text != "(未启动)")
            {
                Clipboard.SetText(TxtUrl.Text);
                AppendLog("已复制访问地址到剪贴板");
            }
        }
        catch { /* 剪贴板被占用时静默 */ }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_server != null && _server.IsRunning) return;

        // 校验
        if (!int.TryParse(TxtPort.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("端口无效 (1-65535)", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(TxtRoot.Text) || !Directory.Exists(TxtRoot.Text))
        {
            MessageBox.Show($"根目录不存在: {TxtRoot.Text}", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 持久化配置
        _cfg.Http.Port     = port;
        _cfg.Http.RootDir  = TxtRoot.Text;
        try { _cfg.Save(); } catch { /* 配置文件被占用不影响启动 */ }

        // 启动服务
        _server = new HttpFileServer(_cfg.Http);
        _server.OnLog   += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _server.OnState += run => Dispatcher.Invoke(() => OnRunningChanged(run));

        try
        {
            _server.StartAsync();
            TxtUrl.Text    = $"http://{GetLocalIP()}:{port}";
            TxtUrl.Foreground = System.Windows.Media.Brushes.Green;
            TxtPort.IsEnabled   = false;
            TxtRoot.IsEnabled   = false;
            BtnStart.IsEnabled  = false;
            BtnStop.IsEnabled   = true;
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            _server?.Dispose();
            _server = null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _server?.Stop();
        _server?.Dispose();
        _server = null;
        OnRunningChanged(false);
    }

    private void OnRunningChanged(bool running)
    {
        TxtPort.IsEnabled  = !running;
        TxtRoot.IsEnabled  = !running;
        BtnStart.IsEnabled = !running;
        BtnStop.IsEnabled  = running;

        if (!running)
        {
            TxtUrl.Text = "(未启动)";
            TxtUrl.Foreground = System.Windows.Media.Brushes.Gray;
        }
        OnStateChanged?.Invoke(running);
    }

    private static string GetLocalIP()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
