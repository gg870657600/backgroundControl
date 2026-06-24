using System.Windows;

namespace backgroundControl;

/// <summary>
/// 工具弹窗 —— HTTP 文件服务 / FTP 服务 / iperf3 打流。
/// 阶段 1：仅窗口骨架 + 状态栏占位。
/// 后续阶段填充：HttpFileServer（阶段 2）、FtpServerHost（阶段 3）、IperfRunner（阶段 4）。
/// </summary>
public partial class ToolsWindow : Window
{
    public ToolsWindow()
    {
        InitializeComponent();
    }
}
