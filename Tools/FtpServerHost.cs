using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace backgroundControl.Tools;

/// <summary>自写 FTP 服务，零外部依赖。支持匿名登录、PASV/PORT、ASCII/Binary、UTF-8、被动端口范围、REST 断点续传。</summary>
public class FtpServerHost : IDisposable
{
    private readonly FtpConfig _cfg;
    private TcpListener? _controlListener;
    private CancellationTokenSource? _cts;
    private bool _running;
    private readonly List<FtpClientSession> _sessions = new();
    private readonly object _sessionsLock = new();
    private int _nextPassivePort;
    private readonly Random _rng = new();

    public event Action<string>? OnLog;
    public event Action<bool>?   OnState;

    public bool IsRunning => _running;

    public FtpServerHost(FtpConfig cfg)
    {
        _cfg = cfg;
        _nextPassivePort = cfg.PassiveStart;
    }

    public void Start()
    {
        if (_running) return;
        if (!Directory.Exists(_cfg.RootDir))
            throw new DirectoryNotFoundException($"RootDir 不存在: {_cfg.RootDir}");
        if (_cfg.PassiveStart > _cfg.PassiveEnd)
            throw new ArgumentException("PassiveStart 必须 ≤ PassiveEnd");

        _cts = new CancellationTokenSource();
        _controlListener = new TcpListener(IPAddress.Any, _cfg.Port);
        _controlListener.Start();
        _running = true;
        OnState?.Invoke(true);
        LogInfo($"FTP 服务已启动: 端口 {_cfg.Port} (根目录: {_cfg.RootDir}, 被动端口 {_cfg.PassiveStart}-{_cfg.PassiveEnd}, 匿名登录: {(_cfg.AllowAnonymous ? "是" : "否")})");

        Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (_running)
        {
            TcpClient? client = null;
            try { client = await _controlListener!.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { LogError($"Accept 失败: {ex.Message}"); continue; }

            var session = new FtpClientSession(this, client, _cfg);
            lock (_sessionsLock) _sessions.Add(session);
            _ = Task.Run(() => session.ProcessAsync(_cts!.Token));
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        try { _controlListener?.Stop(); } catch { }
        lock (_sessionsLock)
        {
            foreach (var s in _sessions) s.Close();
            _sessions.Clear();
        }
        OnState?.Invoke(false);
        LogInfo("FTP 服务已停止");
    }

    /// <summary>从被动端口范围里取一个未被占用的端口</summary>
    internal int AllocatePassivePort()
    {
        int range = _cfg.PassiveEnd - _cfg.PassiveStart + 1;
        for (int i = 0; i < range; i++)
        {
            int port = _nextPassivePort++;
            if (_nextPassivePort > _cfg.PassiveEnd) _nextPassivePort = _cfg.PassiveStart;
            if (TryBindPassive(port)) return port;
        }
        throw new InvalidOperationException("没有可用的被动模式端口");
    }

    private static bool TryBindPassive(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch { return false; }
    }

    internal void RemoveSession(FtpClientSession s)
    {
        lock (_sessionsLock) _sessions.Remove(s);
    }

    internal void LogInfo(string m)  => Log("INFO ", m);
    internal void LogWarn(string m)  => Log("WARN ", m);
    internal void LogError(string m) => Log("ERROR", m);
    private void Log(string level, string msg)
    {
        var prefix = level == "INFO " ? "" : level + " ";
        OnLog?.Invoke($"{prefix}{msg}");
        if      (level == "ERROR") ToolsLogger.Error(ToolsLogger.Source.Ftp, msg);
        else if (level == "WARN ") ToolsLogger.Warn (ToolsLogger.Source.Ftp, msg);
        else                       ToolsLogger.Info (ToolsLogger.Source.Ftp, msg);
    }

    public void Dispose() => Stop();
}

/// <summary>一个 FTP 客户端连接的处理状态</summary>
internal class FtpClientSession
{
    private readonly FtpServerHost _host;
    private readonly FtpConfig _cfg;
    private readonly TcpClient _control;
    private readonly NetworkStream _ctrlStream;
    private bool _authenticated;
    private string _username = "";
    private string _currentDir = "/";
    private bool _isAscii;
    private long _restartOffset;
    private TcpListener? _dataListener;
    private TcpClient?  _dataClient;
    private NetworkStream? _dataStream;
    private string _dataHost = "";
    private int _dataPort;
    private bool _usePasv = true;

    public FtpClientSession(FtpServerHost host, TcpClient client, FtpConfig cfg)
    {
        _host = host;
        _cfg = cfg;
        _control = client;
        _ctrlStream = client.GetStream();
    }

    public async Task ProcessAsync(CancellationToken ct)
    {
        var remote = _control.Client.RemoteEndPoint?.ToString() ?? "?";
        _host.LogInfo($"FTP 连接: {remote}");

        try
        {
            await SendAsync("220 BackgroundControl FTP Server ready. (anonymous)");

            var lineBuf = new StringBuilder();
            var byteBuf = new byte[1024];
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = await _ctrlStream.ReadAsync(byteBuf, 0, byteBuf.Length, ct); }
                catch (Exception ex) { _host.LogInfo($"FTP 客户端断开: {ex.Message}"); break; }
                if (n == 0) break;

                lineBuf.Append(Encoding.ASCII.GetString(byteBuf, 0, n));
                while (true)
                {
                    var s = lineBuf.ToString();
                    int nl = s.IndexOf('\n');
                    if (nl < 0) break;
                    var line = s.Substring(0, nl).TrimEnd('\r').Trim();
                    lineBuf.Remove(0, nl + 1);
                    if (string.IsNullOrEmpty(line)) continue;
                    await ProcessCommandAsync(line);
                    if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) return;
                }
            }
        }
        catch (Exception ex) { _host.LogWarn($"FTP 会话异常: {ex.Message}"); }
        finally
        {
            Close();
            _host.RemoveSession(this);
        }
    }

    private async Task ProcessCommandAsync(string line)
    {
        var parts = line.Split(' ', 2);
        var cmd  = parts[0].ToUpperInvariant();
        var arg  = parts.Length > 1 ? parts[1] : "";

        try
        {
            switch (cmd)
            {
                case "USER":  await OnUser(arg); break;
                case "PASS":  await OnPass(arg); break;
                case "QUIT":  await SendAsync("221 Goodbye."); break;
                case "PWD":
                case "XPWD":  await SendAsync($"257 \"{_currentDir}\" is current directory."); break;
                case "CWD":
                case "XCWD":  await OnCwd(arg); break;
                case "CDUP":
                case "XCUP":  await OnCwd(".."); break;
                case "LIST":  await OnList(arg, nameOnly: false); break;
                case "NLST":  await OnList(arg, nameOnly: true); break;
                case "RETR":  await OnRetr(arg); break;
                case "STOR":  await OnStor(arg); break;
                case "DELE":  await OnDele(arg); break;
                case "MKD":
                case "XMKD":  await OnMkd(arg); break;
                case "RMD":
                case "XRMD":  await OnRmd(arg); break;
                case "TYPE":  await OnType(arg); break;
                case "PASV":  await OnPasv(); break;
                case "PORT":  await OnPort(arg); break;
                case "SIZE":  await OnSize(arg); break;
                case "REST":  await OnRest(arg); break;
                case "NOOP":  await SendAsync("200 NOOP ok."); break;
                case "SYST":  await SendAsync("215 UNIX Type: L8 (Windows backend)"); break;
                case "FEAT":  await OnFeat(); break;
                case "OPTS":  await OnOpts(arg); break;
                case "STAT":  await SendAsync("211 BackgroundControl FTP Server status: OK"); break;
                case "HELP":  await SendAsync("214 USER PASS QUIT PWD CWD LIST RETR STOR TYPE PASV PORT"); break;
                case "ALLO":  await SendAsync("200 ALLO ok."); break;
                case "RNFR":  await SendAsync("350 RNFR accepted; send RNTO."); break;
                case "RNTO":  await SendAsync("550 Rename not supported."); break;
                case "ABOR":  await SendAsync("226 ABOR command successful."); break;
                default:      await SendAsync($"502 Command not implemented: {cmd}"); break;
            }
        }
        catch (Exception ex)
        {
            _host.LogError($"命令 {cmd} ({arg}) 异常: {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace}");
            try { await SendAsync($"550 Internal error: {ex.Message}"); } catch { }
        }
    }

    // ==================== 命令实现 ====================

    private async Task OnUser(string arg)
    {
        _username = arg;
        if (_cfg.AllowAnonymous)
        {
            _authenticated = true;
            _currentDir = "/";
            await SendAsync("230 Anonymous login successful.");
        }
        else
        {
            await SendAsync("331 Password required for " + arg + ".");
        }
    }

    private async Task OnPass(string arg)
    {
        // 当前实现：只要 AllowAnonymous=true，任何密码都通过
        // 严格密码校验留给后续
        _authenticated = true;
        await SendAsync($"230 Login successful ({_username}).");
    }

    private async Task OnCwd(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        try
        {
            var newPath = ResolvePath(arg);
            if (!Directory.Exists(newPath)) { await SendAsync("550 Directory not found."); return; }
            _currentDir = ToFtpPath(newPath);
            await SendAsync($"250 CWD successful. \"{_currentDir}\"");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 CWD error: {ex.Message}"); }
    }

    private async Task OnList(string arg, bool nameOnly)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        string dir;
        try { dir = string.IsNullOrWhiteSpace(arg) ? ResolvePath(".") : ResolvePath(arg); }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); return; }
        if (!Directory.Exists(dir)) { await SendAsync("550 Directory not found."); return; }

        if (!await OpenDataAsync()) return;
        await SendAsync("150 Opening data connection.");

        try
        {
            using var w = new StreamWriter(_dataStream!, new UTF8Encoding(false));
            // 先写 . 和 ..
            if (!nameOnly)
            {
                await w.WriteLineAsync(BuildEntryLine(".", dir, true));
                await w.WriteLineAsync(BuildEntryLine("..", Path.GetDirectoryName(dir) ?? dir, true));
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var name = Path.GetFileName(entry);
                if (nameOnly)
                {
                    await w.WriteLineAsync(name);
                }
                else
                {
                    var isDir = Directory.Exists(entry) && !File.Exists(entry);
                    await w.WriteLineAsync(BuildEntryLine(name, entry, isDir));
                }
            }
            await w.FlushAsync();
            await SendAsync("226 Transfer complete.");
        }
        catch (Exception ex)
        {
            await SendAsync($"550 List error: {ex.Message}");
        }
        finally { CloseData(); }
    }

    private static string BuildEntryLine(string name, string fullPath, bool isDir)
    {
        var info = isDir ? (FileSystemInfo)new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        var perm = isDir ? "drwxr-xr-x" : "-rw-r--r--";
        long size = isDir ? 4096 : ((FileInfo)info).Length;
        var dt = info.LastWriteTime;
        // Unix 风格时间格式
        var dtStr = dt.Year != DateTime.Now.Year
            ? dt.ToString("MMM dd yyyy", CultureInfo.InvariantCulture)
            : dt.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture);
        return $"{perm}   1 user     group     {size,10} {dtStr} {name}";
    }

    private async Task OnRetr(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        if (string.IsNullOrEmpty(arg)) { await SendAsync("501 Missing filename."); return; }
        try
        {
            var path = ResolvePath(arg);
            if (!File.Exists(path)) { await SendAsync("550 File not found."); return; }
            if (Directory.Exists(path)) { await SendAsync("550 Is a directory, use RETR on file."); return; }

            if (!await OpenDataAsync()) return;
            await SendAsync("150 Opening data connection.");

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (_restartOffset > 0 && _restartOffset < fs.Length) fs.Seek(_restartOffset, SeekOrigin.Begin);
            await fs.CopyToAsync(_dataStream!, 81920);
            await _dataStream!.FlushAsync();
            _restartOffset = 0;
            await SendAsync("226 Transfer complete.");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 RETR error: {ex.Message}"); }
        finally { CloseData(); }
    }

    private async Task OnStor(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        if (string.IsNullOrEmpty(arg)) { await SendAsync("501 Missing filename."); return; }
        try
        {
            var path = ResolvePath(arg);
            // 二次校验路径
            if (!IsInsideRoot(path)) { await SendAsync("550 Permission denied."); return; }
            // 防止把目录覆盖成文件
            if (Directory.Exists(path)) { await SendAsync("550 Is a directory."); return; }

            if (!await OpenDataAsync()) return;
            await SendAsync("150 Opening data connection.");

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await _dataStream!.CopyToAsync(fs, 81920);
            await fs.FlushAsync();
            await SendAsync("226 Transfer complete.");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 STOR error: {ex.Message}"); }
        finally { CloseData(); }
    }

    private async Task OnDele(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        if (string.IsNullOrEmpty(arg)) { await SendAsync("501 Missing filename."); return; }
        try
        {
            var path = ResolvePath(arg);
            if (!File.Exists(path)) { await SendAsync("550 File not found."); return; }
            File.Delete(path);
            await SendAsync("250 File deleted.");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 DELE error: {ex.Message}"); }
    }

    private async Task OnMkd(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        if (string.IsNullOrEmpty(arg)) { await SendAsync("501 Missing dirname."); return; }
        try
        {
            var path = ResolvePath(arg);
            Directory.CreateDirectory(path);
            await SendAsync($"257 \"{arg}\" created.");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 MKD error: {ex.Message}"); }
    }

    private async Task OnRmd(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        if (string.IsNullOrEmpty(arg)) { await SendAsync("501 Missing dirname."); return; }
        try
        {
            var path = ResolvePath(arg);
            if (!Directory.Exists(path)) { await SendAsync("550 Directory not found."); return; }
            Directory.Delete(path, recursive: false);
            await SendAsync("250 Directory removed.");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 RMD error: {ex.Message}"); }
    }

    private async Task OnType(string arg)
    {
        var a = arg.ToUpperInvariant();
        if (a == "A") { _isAscii = true;  await SendAsync("200 Type set to ASCII."); }
        else if (a == "I") { _isAscii = false; await SendAsync("200 Type set to Binary."); }
        else await SendAsync("504 TYPE supports only A or I.");
    }

    private async Task OnPasv()
    {
        try
        {
            int port = _host.AllocatePassivePort();
            _dataListener = new TcpListener(IPAddress.Any, port);
            _dataListener.Start();
            _usePasv = true;
            // 0,0,0,0 让客户端用控制连接的目标 IP 主动连过来（NAT 友好）
            await SendAsync($"227 Entering Passive Mode (0,0,0,0,{port / 256},{port % 256}).");
        }
        catch (Exception ex) { await SendAsync($"425 Cannot open passive port: {ex.Message}"); }
    }

    private async Task OnPort(string arg)
    {
        try
        {
            var parts = arg.Split(',');
            if (parts.Length != 6) { await SendAsync("501 Invalid PORT (need h1,h2,h3,h4,p1,p2)."); return; }
            _dataHost = $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
            _dataPort = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);
            CloseData();
            _usePasv = false;
            await SendAsync("200 PORT command successful.");
        }
        catch (Exception ex) { await SendAsync($"501 PORT error: {ex.Message}"); }
    }

    private async Task OnSize(string arg)
    {
        if (!_authenticated) { await SendAsync("530 Please login."); return; }
        try
        {
            var path = ResolvePath(arg);
            if (!File.Exists(path)) { await SendAsync("550 File not found."); return; }
            var len = new FileInfo(path).Length;
            await SendAsync($"213 {len}");
        }
        catch (UnauthorizedAccessException) { await SendAsync("550 Permission denied."); }
        catch (Exception ex) { await SendAsync($"550 SIZE error: {ex.Message}"); }
    }

    private async Task OnRest(string arg)
    {
        if (long.TryParse(arg, out var off) && off >= 0)
        {
            _restartOffset = off;
            await SendAsync($"350 Restart position accepted ({off}).");
        }
        else await SendAsync("501 Invalid REST offset.");
    }

    private async Task OnFeat()
    {
        await SendAsync("211-Features:");
        await SendAsync(" PASV");
        await SendAsync(" PORT");
        await SendAsync(" SIZE");
        await SendAsync(" REST STREAM");
        await SendAsync(" UTF8");
        await SendAsync("211 End");
    }

    private async Task OnOpts(string arg)
    {
        if (arg.Equals("UTF8 ON", StringComparison.OrdinalIgnoreCase))
            await SendAsync("200 UTF8 set to on.");
        else
            await SendAsync("501 Option not understood.");
    }

    // ==================== 数据连接 ====================

    private async Task<bool> OpenDataAsync()
    {
        if (_usePasv)
        {
            if (_dataListener == null) { await SendAsync("425 PASV not set."); return false; }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _dataClient = await _dataListener.AcceptTcpClientAsync(cts.Token);
                _dataStream = _dataClient.GetStream();
                return true;
            }
            catch (OperationCanceledException) { await SendAsync("425 Data connection timeout."); return false; }
            catch (Exception ex) { await SendAsync($"425 Cannot accept: {ex.Message}"); return false; }
        }
        else
        {
            try
            {
                _dataClient = new TcpClient();
                await _dataClient.ConnectAsync(_dataHost, _dataPort);
                _dataStream = _dataClient.GetStream();
                return true;
            }
            catch (Exception ex) { await SendAsync($"425 Cannot connect: {ex.Message}"); return false; }
        }
    }

    private void CloseData()
    {
        try { _dataStream?.Dispose(); } catch { }
        try { _dataClient?.Close();   } catch { }
        try { _dataListener?.Stop();  } catch { }
        _dataStream = null;
        _dataClient = null;
        _dataListener = null;
    }

    // ==================== 路径处理 ====================

    private string ResolvePath(string userPath)
    {
        // 去掉 FTP 路径里可能含的 ".." / "." / 多余斜杠
        var norm = userPath.Replace('\\', '/');
        // 把 FTP 绝对路径 /x/y 映射到 rootDir\x\y；相对路径 x/y 映射到 currentDir\x\y
        string rel;
        if (norm.StartsWith("/"))
            rel = norm.TrimStart('/');
        else
        {
            // _currentDir 已经是 FTP 路径（"/" 或 "/subdir"），不能再 ToFtpPath()
            var current = _currentDir.TrimStart('/');
            rel = (current + "/" + norm).TrimStart('/');
        }
        var rootFull = GetRootFull();
        var combined = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideRoot(combined))
            throw new UnauthorizedAccessException("Path traversal");
        return combined;
    }

    private string ToFtpPath(string localPath)
    {
        var rootFull = GetRootFull();
        if (localPath.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return "/";
        var rel = localPath.Substring(rootFull.Length).Replace('\\', '/').TrimStart('/');
        return string.IsNullOrEmpty(rel) ? "/" : "/" + rel;
    }

    private string GetRootFull()
    {
        return Path.GetFullPath(_cfg.RootDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private bool IsInsideRoot(string combined)
    {
        var rootFull = GetRootFull();
        return combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SendAsync(string msg)
    {
        var bytes = Encoding.ASCII.GetBytes(msg + "\r\n");
        try { await _ctrlStream.WriteAsync(bytes); }
        catch (Exception ex) { _host.LogWarn($"FTP 发送失败: {ex.Message}"); }
    }

    public void Close()
    {
        CloseData();
        try { _ctrlStream.Dispose(); } catch { }
        try { _control.Close();       } catch { }
    }
}
