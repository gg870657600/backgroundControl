using System.Windows;
using System.Windows.Documents;

namespace backgroundControl;

/// <summary>
/// 工具弹窗 —— HTTP 文件服务 / FTP 服务 / iperf3 打流。
/// 阶段 1：窗口骨架 + 状态栏。
/// 阶段 2：HTTP 文件服务（自写 HttpFileServer）。
/// 阶段 3/4：FTP / iperf3 将在后续阶段填充。
/// </summary>
public partial class ToolsWindow : Window
{
    public ToolsWindow()
    {
        InitializeComponent();
    }

    private void HttpCtrl_StateChanged(bool running)
    {
        HttpStatusRun.Text       = running ? "运行中" : "已停止";
        HttpStatusRun.Foreground = running
            ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71))
            : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    private void HttpCtrl_LogAppended(string line)
    {
        // 透传到 ToolsLogger（订阅者在主窗口或其他地方）
        // 现阶段只写本地 console
        System.Diagnostics.Debug.WriteLine($"[HTTP] {line.Trim()}");
    }

    private void FtpCtrl_StateChanged(bool running)
    {
        FtpStatusRun.Text       = running ? "运行中" : "已停止";
        FtpStatusRun.Foreground = running
            ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71))
            : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    private void FtpCtrl_LogAppended(string line)
    {
        System.Diagnostics.Debug.WriteLine($"[FTP] {line.Trim()}");
    }

    private void IperfCtrl_StateChanged(bool running)
    {
        IperfStatusRun.Text       = running ? "运行中" : "已停止";
        IperfStatusRun.Foreground = running
            ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71))
            : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C));
    }

    private void IperfCtrl_LogAppended(string line)
    {
        System.Diagnostics.Debug.WriteLine($"[iperf3] {line.Trim()}");
    }
}
