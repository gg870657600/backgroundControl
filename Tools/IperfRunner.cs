using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace backgroundControl.Tools;

public class IperfRunner : IDisposable
{
    private Process? _proc;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _stdout = new();
    private bool _isClient;

    public bool IsRunning => _proc is { HasExited: false };

    public event Action<string>? OnLog;
    public event Action<bool>?   OnState;
    public event Action<IperfIntervalData>? OnInterval;
    public event Action<IperfFinalResult>?  OnFinal;

    public void StartServer(int port)
    {
        KillIperfProcesses();
        StartProcess($"-s -1 -p {port} -i 1 --forceflush");
    }

    private static void KillIperfProcesses()
    {
        // 启动 Server 前清理残留的 iperf3 进程，释放端口
        foreach (var p in Process.GetProcessesByName("iperf3"))
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }
    }

    public void StartClient(string host, int port, int duration, int parallel,
        bool udp, string bandwidth, int interval)
    {
        _isClient = true;
        var proto = udp ? "-u" : "";
        var bw = udp && !string.IsNullOrWhiteSpace(bandwidth) ? $"-b {bandwidth}" : "";
        StartProcess($"-c {host} -p {port} -t {duration} -P {parallel} -i {interval} {proto} {bw} --forceflush");
    }

    private void StartProcess(string args)
    {
        Stop();
        _stdout.Clear();

        var exe = IperfLocator.GetIperfExePath();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var psi = new ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            WorkingDirectory       = Path.GetDirectoryName(exe),
        };

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        OnLog?.Invoke($"> {Path.GetFileName(exe)} {args}");
        _proc.Start();

        var stdoutTask = ReadStreamAsync(_proc.StandardOutput, false, ct);
        var stderrTask = ReadStreamAsync(_proc.StandardError, true, ct);

        _proc.Exited += (_, _) =>
        {
            try { _proc?.WaitForExit(5000); } catch { }

            if (!ct.IsCancellationRequested)
            {
                ParseFinalResult(_stdout.ToString());
                OnState?.Invoke(false);
            }
            DisposeProc();
        };

        OnState?.Invoke(true);
    }

    private async Task ReadStreamAsync(StreamReader reader, bool isStderr, CancellationToken ct)
    {
        var buf = new char[1];
        var line = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var n = await reader.ReadAsync(buf, 0, 1).ConfigureAwait(false);
                if (n == 0) break;

                var c = buf[0];
                if (c == '\n')
                {
                    var text = line.ToString().TrimEnd('\r');
                    line.Clear();
                    if (text.Length > 0)
                        ProcessLine(text, isStderr);
                }
                else
                {
                    line.Append(c);
                }
            }

            if (line.Length > 0)
            {
                var text = line.ToString().TrimEnd('\r');
                if (text.Length > 0)
                    ProcessLine(text, isStderr);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { OnLog?.Invoke($"[ReadStream] {ex.Message}"); }
    }

    private void ProcessLine(string text, bool isStderr)
    {
        if (isStderr)
            OnLog?.Invoke($"[STDERR] {text}");
        else
        {
            _stdout.AppendLine(text);
            OnLog?.Invoke(text);
        }
        TryParseTextInterval(text);
    }

    private static readonly Regex IntervalRegex = new(
        @"\[\s*\d+\]\s+([\d.]+)-([\d.]+)\s+sec\s+([\d.]+)\s+(\w+)\s+([\d.]+)\s+(\w+/sec)",
        RegexOptions.Compiled);

    private static readonly Regex JitterRegex = new(@"([\d.]+)\s+ms", RegexOptions.Compiled);
    private static readonly Regex LossRegex   = new(@"(\d+)/(\d+)\s*\(([\d.]+)%\)", RegexOptions.Compiled);

    private void TryParseTextInterval(string line)
    {
        var m = IntervalRegex.Match(line);
        if (!m.Success) return;

        var bw = ParseBitsPerSec(m.Groups[5].Value, m.Groups[6].Value);
        // iperf3 stdout 末尾的 sender/receiver 汇总行带 "sender" / "receiver" 关键字
        var isSenderLine   = line.Contains("sender",   StringComparison.OrdinalIgnoreCase);
        var isReceiverLine = line.Contains("receiver", StringComparison.OrdinalIgnoreCase);

        // 中间 interval 行：client 模式 = receiver 实时，server 模式 = sender 实时
        // 末尾汇总行：按关键字覆盖对应字段
        var data = new IperfIntervalData
        {
            BitsPerSec         = isSenderLine   ? bw : (_isClient ? 0 : bw),
            ReceiverBitsPerSec = isReceiverLine ? bw : (_isClient ? bw : 0),
        };

        var jm = JitterRegex.Match(line);
        if (jm.Success) data.JitterMs = double.Parse(jm.Groups[1].Value);

        var lm = LossRegex.Match(line);
        if (lm.Success)
        {
            data.LostPackets  = long.Parse(lm.Groups[1].Value);
            data.TotalPackets = long.Parse(lm.Groups[2].Value);
            data.LossPercent  = double.Parse(lm.Groups[3].Value);
        }

        OnInterval?.Invoke(data);
    }

    private void ParseFinalResult(string output)
    {
        var result = new IperfFinalResult();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = IntervalRegex.Match(line);
            if (!m.Success) continue;

            var bw = ParseBitsPerSec(m.Groups[5].Value, m.Groups[6].Value);
            if (line.Contains("sender", StringComparison.OrdinalIgnoreCase))
                result.SenderMbps = bw / 1_000_000.0;
            if (line.Contains("receiver", StringComparison.OrdinalIgnoreCase))
                result.ReceiverMbps = bw / 1_000_000.0;
        }

        if (result.SenderMbps > 0 || result.ReceiverMbps > 0)
            OnFinal?.Invoke(result);
    }

    private static double ParseBitsPerSec(string value, string unit)
    {
        var v = double.Parse(value);
        return unit.ToLowerInvariant() switch
        {
            "bits/sec"   => v,
            "kbits/sec"  => v * 1_000,
            "mbits/sec"  => v * 1_000_000,
            "gbits/sec"  => v * 1_000_000_000,
            _ => v,
        };
    }

    public void Stop()
    {
        if (_proc is { HasExited: false })
        {
            try { _proc.Kill(entireProcessTree: true); } catch { }
        }
        _cts?.Cancel();
        DisposeProc();
        OnState?.Invoke(false);
    }

    private void DisposeProc()
    {
        _proc?.Dispose();
        _proc = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}

public class IperfIntervalData
{
    public double BitsPerSec         { get; set; }
    public double ReceiverBitsPerSec { get; set; }
    public double JitterMs           { get; set; }
    public long   LostPackets        { get; set; }
    public long   TotalPackets       { get; set; }
    public double LossPercent        { get; set; }

    public string SenderMbps   => $"{BitsPerSec / 1_000_000.0:F2}";
    public string ReceiverMbps => $"{ReceiverBitsPerSec / 1_000_000.0:F2}";
}

public class IperfFinalResult
{
    public double SenderMbps   { get; set; }
    public double ReceiverMbps { get; set; }
}
