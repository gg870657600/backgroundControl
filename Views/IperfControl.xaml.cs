using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using backgroundControl.Tools;

using MessageBox = System.Windows.MessageBox;

namespace backgroundControl.Views;

public partial class IperfControl : System.Windows.Controls.UserControl
{
    private IperfRunner? _runner;
    private readonly ToolsConfig _cfg;
    private readonly List<IperfIntervalData> _intervals = new();

    // 曲线图数据
    private readonly List<double> _chartData = new();
    private readonly List<System.Windows.Shapes.Ellipse> _chartDots = new();
    private double _chartMaxMbps = 1.0;
    private int _iperfDuration;
    private bool _isClientMode;

    public event Action<bool>?   OnStateChanged;
    public event Action<string>? OnLogAppended;

    public void StopService()
    {
        _runner?.Stop();
        _runner?.Dispose();
        _runner = null;
    }

    public IperfControl()
    {
        InitializeComponent();
        Mode_Changed(null, null!);
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
        ProtocolRow.Visibility = isClient ? Visibility.Visible : Visibility.Collapsed;
        var isTcp = isClient && CbProtocol.SelectedIndex == 0;
        var showBw = isClient && CbProtocol.SelectedIndex == 1;
        LblBandwidth.Visibility = showBw ? Visibility.Visible : Visibility.Collapsed;
        TxtBandwidth.Visibility = showBw ? Visibility.Visible : Visibility.Collapsed;

        LblTcpWin.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        CkTcpWin.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        TxtTcpWinSize.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        CbTcpWinUnit.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        if (isTcp) TcpWin_CheckedChanged(null, null!);

        var isSrv = !isClient;
        LblTcpWinSrv.Visibility = isSrv ? Visibility.Visible : Visibility.Collapsed;
        CkTcpWinSrv.Visibility = isSrv ? Visibility.Visible : Visibility.Collapsed;
        TxtTcpWinSizeSrv.Visibility = isSrv ? Visibility.Visible : Visibility.Collapsed;
        CbTcpWinUnitSrv.Visibility = isSrv ? Visibility.Visible : Visibility.Collapsed;
        if (isSrv) TcpWinSrv_CheckedChanged(null, null!);
    }

    private void TcpWin_CheckedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = CkTcpWin.IsChecked == true;
        TxtTcpWinSize.IsEnabled = enabled;
        CbTcpWinUnit.IsEnabled = enabled;
    }

    private void TcpWinSrv_CheckedChanged(object sender, RoutedEventArgs e)
    {
        var enabled = CkTcpWinSrv.IsChecked == true;
        TxtTcpWinSizeSrv.IsEnabled = enabled;
        CbTcpWinUnitSrv.IsEnabled = enabled;
    }

    private void Protocol_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LblBandwidth == null || TxtBandwidth == null) return;
        var isUdp = CbProtocol.SelectedIndex == 1;
        var isTcp = CbProtocol.SelectedIndex == 0;

        LblBandwidth.Visibility = isUdp ? Visibility.Visible : Visibility.Collapsed;
        TxtBandwidth.Visibility = isUdp ? Visibility.Visible : Visibility.Collapsed;

        LblTcpWin.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        CkTcpWin.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        TxtTcpWinSize.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        CbTcpWinUnit.Visibility = isTcp ? Visibility.Visible : Visibility.Collapsed;
        if (isTcp) TcpWin_CheckedChanged(null, null!);
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
                if (udp && par > 1)
                {
                    MessageBox.Show("UDP 模式不支持并行流（Cygwin iperf3 限制），请将并行流数设为 1 或改用 TCP", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _runner.Dispose();
                    _runner = null;
                    return;
                }
                var bw = udp ? TxtBandwidth.Text.Trim() : "";
                var tcpWin = (!udp && CkTcpWin.IsChecked == true) ? $"{TxtTcpWinSize.Text.Trim()}{((ComboBoxItem)CbTcpWinUnit.SelectedItem).Content.ToString()![0]}" : null;
                _iperfDuration = dur;
                _isClientMode = true;
                _runner.StartClient(host, port, dur, par, udp, bw, 1, tcpWin);
            }
            else
            {
                var tcpWin = CkTcpWinSrv.IsChecked == true ? $"{TxtTcpWinSizeSrv.Text.Trim()}{((ComboBoxItem)CbTcpWinUnitSrv.SelectedItem).Content.ToString()![0]}" : null;
                _iperfDuration = 0;
                _isClientMode = false;
                _runner.StartServer(port, tcpWin);
            }

            ResetChart();
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

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_runner == null) return;
        AppendLog("正在停止…");
        await Task.Run(() => _runner.Stop());
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
        var logLine = $"[{idx,3}s]  ↓{iv.ReceiverMbps} Mbps  ↑{iv.SenderMbps} Mbps";
        if (iv.JitterMs > 0)
            logLine += $"   抖动 {iv.JitterMs:F3}ms   丢包 {iv.LossPercent:F2}%";
        TxtRealtime.Text = logLine;
        AppendLog(logLine);

        // 推点到曲线
        var mbps = _isClientMode
            ? iv.BitsPerSec / 1_000_000.0
            : iv.ReceiverBitsPerSec / 1_000_000.0;
        _chartData.Add(mbps);
        if (mbps > _chartMaxMbps) _chartMaxMbps = mbps;
        UpdateChart();
    }

    private void OnFinalReceived(IperfFinalResult fr)
    {
        TxtResult.Text = $"发送 {fr.SenderMbps:F1} / 接收 {fr.ReceiverMbps:F1} Mbps";
        TxtRealtime.Text = $"完成  ↓{fr.ReceiverMbps:F1} Mbps  ↑{fr.SenderMbps:F1} Mbps";
        AppendLog("");
        AppendLog($"═══ 完成  ↓{fr.ReceiverMbps:F1} Mbps  ↑{fr.SenderMbps:F1} Mbps ═══");
    }

    private void ResetChart()
    {
        _chartData.Clear();
        _chartMaxMbps = 1.0;
        if (TxtChartMax != null) TxtChartMax.Text = "准备…";
        if (TxtChartLabel != null)
            TxtChartLabel.Text = _isClientMode ? "发送 (Mbps)" : "接收 (Mbps)";
        foreach (var dot in _chartDots) ChartCanvas.Children.Remove(dot);
        _chartDots.Clear();
        if (ChartLine != null)
        {
            ChartLine.Points = new PointCollection();
            ChartLine.Stroke = _isClientMode
                ? System.Windows.Media.Brushes.DodgerBlue
                : System.Windows.Media.Brushes.LimeGreen;
        }
    }

    private void UpdateChart()
    {
        if (ChartLine == null || ChartCanvas == null) return;
        var w = ChartCanvas.ActualWidth;
        var h = ChartCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var n = _chartData.Count;
        if (n == 0)
        {
            ChartLine.Points = new PointCollection();
            foreach (var dot in _chartDots) ChartCanvas.Children.Remove(dot);
            _chartDots.Clear();
            return;
        }

        var xMax = n;
        var xStep = w / Math.Max(1, xMax - 1);
        var yMax = Math.Max(0.1, _chartMaxMbps) * 1.2;

        var pts = new PointCollection();
        for (int i = 0; i < n; i++)
        {
            double x = i * xStep;
            double y = h - Math.Min(1.0, _chartData[i] / yMax) * h;
            pts.Add(new System.Windows.Point(x, y));
        }
        ChartLine.Points = pts;

        foreach (var dot in _chartDots) ChartCanvas.Children.Remove(dot);
        _chartDots.Clear();

        var label = _isClientMode ? "发送" : "接收";
        const double dotR = 4;
        for (int i = 0; i < n; i++)
        {
            double x = i * xStep;
            double y = h - Math.Min(1.0, _chartData[i] / yMax) * h;
            var dot = new Ellipse
            {
                Width = dotR * 2,
                Height = dotR * 2,
                Fill = System.Windows.Media.Brushes.Red,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"第 {i + 1} 秒\n{label}  {_chartData[i]:F1} Mbps",
            };
            ToolTipService.SetInitialShowDelay(dot, 0);
            Canvas.SetLeft(dot, x - dotR);
            Canvas.SetTop(dot, y - dotR);
            ChartCanvas.Children.Add(dot);
            _chartDots.Add(dot);
        }

        if (TxtChartMax != null)
            TxtChartMax.Text = $"峰值 {_chartMaxMbps:F1} Mbps";
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateChart();
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
        var isClient = RbClient.IsChecked == true;
        var isTcp = isClient && CbProtocol.SelectedIndex == 0;
        LblTcpWin.IsEnabled = enabled && isTcp;
        CkTcpWin.IsEnabled = enabled && isTcp;
        TxtTcpWinSize.IsEnabled = enabled && isTcp && CkTcpWin.IsChecked == true;
        CbTcpWinUnit.IsEnabled = enabled && isTcp && CkTcpWin.IsChecked == true;
        var isSrv = !enabled ? false : RbClient.IsChecked == false;
        LblTcpWinSrv.IsEnabled = enabled && isSrv;
        CkTcpWinSrv.IsEnabled = enabled && isSrv;
        TxtTcpWinSizeSrv.IsEnabled = enabled && isSrv && CkTcpWinSrv.IsChecked == true;
        CbTcpWinUnitSrv.IsEnabled = enabled && isSrv && CkTcpWinSrv.IsChecked == true;
        BtnStart.IsEnabled = enabled;
        BtnStop.IsEnabled = !enabled;
        BtnStart.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        BtnStop.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }
}
