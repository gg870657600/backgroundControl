using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using backgroundControl.Tools;

using MessageBox = System.Windows.MessageBox;

namespace backgroundControl.Views;

public partial class IperfControl : System.Windows.Controls.UserControl
{
    private IperfRunner? _runner;
    private readonly ToolsConfig _cfg;
    private readonly List<IperfIntervalData> _intervals = new();

    public event Action<bool>?   OnStateChanged;
    public event Action<string>? OnLogAppended;

    public IperfControl()
    {
        InitializeComponent();
        _cfg = ToolsConfig.Load();
        TxtPort.Text = _cfg.Iperf.DefaultPort.ToString();
        AppendLog("就绪。选择模式并配置参数后点击 启动。");
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (TxtHost == null) return;
        var isClient = RbClient.IsChecked == true;
        TxtHost.IsEnabled = isClient;
        TxtDuration.IsEnabled = isClient;
        TxtParallel.IsEnabled = isClient;
        CbProtocol.IsEnabled = isClient;
        TxtBandwidth.IsEnabled = isClient;
        LblBandwidth.Visibility = isClient && CbProtocol.SelectedIndex == 1
            ? Visibility.Visible : Visibility.Collapsed;
        TxtBandwidth.Visibility = isClient && CbProtocol.SelectedIndex == 1
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Protocol_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LblBandwidth == null || TxtBandwidth == null) return;
        var show = CbProtocol.SelectedIndex == 1;
        LblBandwidth.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TxtBandwidth.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AppendLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        TxtLog.AppendText(line);
        LogScroll.ScrollToEnd();
        OnLogAppended?.Invoke(line);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is { IsRunning: true }) return;

        if (!int.TryParse(TxtPort.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("端口无效 (1-65535)", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cfg.Iperf.DefaultPort = port;
        try { _cfg.Save(); } catch { }

        _intervals.Clear();
        _runner = new IperfRunner();
        _runner.OnLog      += msg => Dispatcher.Invoke(() => AppendLog(msg));
        _runner.OnState    += running => Dispatcher.Invoke(() => OnRunningChanged(running));
        _runner.OnInterval += iv => Dispatcher.Invoke(() => OnIntervalReceived(iv));
        _runner.OnFinal    += fr => Dispatcher.Invoke(() => OnFinalReceived(fr));

        try
        {
            if (RbClient.IsChecked == true)
            {
                var host = TxtHost.Text.Trim();
                if (string.IsNullOrWhiteSpace(host))
                {
                    MessageBox.Show("请输入服务器地址", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _runner.Dispose();
                    _runner = null;
                    return;
                }
                if (!int.TryParse(TxtDuration.Text, out var dur) || dur < 1)
                {
                    MessageBox.Show("测试时长至少 1 秒", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _runner.Dispose();
                    _runner = null;
                    return;
                }
                if (!int.TryParse(TxtParallel.Text, out var par) || par < 1)
                {
                    MessageBox.Show("并行流数至少为 1", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _runner.Dispose();
                    _runner = null;
                    return;
                }

                var udp = CbProtocol.SelectedIndex == 1;
                var bw = udp ? TxtBandwidth.Text.Trim() : "";
                _runner.StartClient(host, port, dur, par, udp, bw, 1);
            }
            else
            {
                _runner.StartServer(port);
            }

            TxtStatus.Text = "运行中…";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Green;
            TxtRealtime.Text = "";
            SetControlsEnabled(false);
        }
        catch (Exception ex)
        {
            AppendLog($"启动失败: {ex.Message}");
            _runner.Dispose();
            _runner = null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_runner == null) return;
        AppendLog("正在停止…");
        _runner.Stop();
        _runner.Dispose();
        _runner = null;
    }

    private void OnRunningChanged(bool running)
    {
        if (!running)
        {
            SetControlsEnabled(true);
            TxtStatus.Text = _runner == null ? "已停止" : "完成";
            TxtStatus.Foreground = System.Windows.Media.Brushes.Gray;
        }
        OnStateChanged?.Invoke(running);
    }

    private void OnIntervalReceived(IperfIntervalData iv)
    {
        _intervals.Add(iv);
        var idx = _intervals.Count;
        TxtRealtime.Text = $"[{idx}s]  ↓{iv.ReceiverMbps} Mbps  ↑{iv.SenderMbps} Mbps";
    }

    private void OnFinalReceived(IperfFinalResult fr)
    {
        TxtResult.Text = $"发送 {fr.SenderMbps:F1} / 接收 {fr.ReceiverMbps:F1} Mbps";
        TxtRealtime.Text = $"完成  ↓{fr.ReceiverMbps:F1} Mbps  ↑{fr.SenderMbps:F1} Mbps";

        AppendLog("");
        AppendLog("═══ 每秒带宽 ═══");
        for (int i = 0; i < _intervals.Count; i++)
        {
            var iv = _intervals[i];
            var line = $"  {i + 1,3}s  ↓{iv.ReceiverMbps} Mbps  ↑{iv.SenderMbps} Mbps";
            if (iv.JitterMs > 0)
                line += $"  抖动 {iv.JitterMs:F3}ms  丢包 {iv.LossPercent:F2}%";
            AppendLog(line);
        }
        AppendLog("════════════════");
        AppendLog($"总计  ↓{fr.ReceiverMbps:F1} Mbps  ↑{fr.SenderMbps:F1} Mbps");
    }

    private void SetControlsEnabled(bool enabled)
    {
        RbClient.IsEnabled = enabled;
        RbServer.IsEnabled = enabled;
        TxtHost.IsEnabled = enabled && RbClient.IsChecked == true;
        TxtPort.IsEnabled = enabled;
        TxtDuration.IsEnabled = enabled && RbClient.IsChecked == true;
        TxtParallel.IsEnabled = enabled && RbClient.IsChecked == true;
        CbProtocol.IsEnabled = enabled && RbClient.IsChecked == true;
        TxtBandwidth.IsEnabled = enabled && RbClient.IsChecked == true;
        BtnStart.IsEnabled = enabled;
        BtnStop.IsEnabled = !enabled;
        BtnStart.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }
}
