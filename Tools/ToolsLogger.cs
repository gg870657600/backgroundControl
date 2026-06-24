using System;
using System.Collections.Concurrent;
using System.Windows;

namespace backgroundControl.Tools;

/// <summary>
/// 三个服务（HTTP/FTP/iperf3）的统一日志入口。
/// 内部用线程安全队列缓存最近 500 行；UI 端订阅 LogAppended 事件自行渲染。
/// </summary>
public static class ToolsLogger
{
    public enum Source { Http, Ftp, Iperf, System }

    public readonly record struct LogLine(DateTime Time, Source Source, string Text);

    private static readonly ConcurrentQueue<LogLine> _buffer = new();
    private const int MaxLines = 500;

    /// <summary>UI 端订阅此事件刷新日志视图（事件在写入线程触发，订阅者自行 Dispatcher.Invoke）</summary>
    public static event Action<LogLine>? LogAppended;

    public static void Info(Source src, string text)  => Append(src, "INFO ", text);
    public static void Warn(Source src, string text)  => Append(src, "WARN ", text);
    public static void Error(Source src, string text) => Append(src, "ERROR", text);
    public static void Debug(Source src, string text) => Append(src, "DEBUG", text);

    private static void Append(Source src, string level, string text)
    {
        var line = new LogLine(DateTime.Now, src, $"[{level}] {text}");
        _buffer.Enqueue(line);
        // 环形截断：超过 MaxLines 弹出最早的
        while (_buffer.Count > MaxLines && _buffer.TryDequeue(out _)) { }

        try
        {
            LogAppended?.Invoke(line);
        }
        catch
        {
            // 订阅者异常不能影响日志写入
        }
    }

    /// <summary>当前缓冲的全部日志（用于初始化 UI）</summary>
    public static LogLine[] Snapshot() => _buffer.ToArray();
}
