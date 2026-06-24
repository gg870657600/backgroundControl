using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace backgroundControl.Tools;

/// <summary>
/// 简易 HTTP 文件服务，提供：
/// - GET /              目录列表 HTML
/// - GET /file          文件下载（支持 HTTP Range 断点续传）
/// - POST /upload       multipart/form-data 上传
/// - DELETE /file       删除文件
/// 零外部依赖，全部基于 System.Net.HttpListener。
/// </summary>
public class HttpFileServer : IDisposable
{
    private readonly HttpConfig _cfg;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _running;

    public event Action<string>? OnLog;
    public event Action<bool>?   OnState;

    public bool   IsRunning => _running;
    public string BaseUrl   => $"http://+:{_cfg.Port}";   // HttpListener 的占位 host

    public HttpFileServer(HttpConfig cfg) => _cfg = cfg;

    public Task StartAsync()
    {
        if (_running) return Task.CompletedTask;

        // RootDir 不存在则抛错，由 UI 层提示用户选择
        if (!Directory.Exists(_cfg.RootDir))
            throw new DirectoryNotFoundException($"RootDir 不存在: {_cfg.RootDir}");

        _listener = new HttpListener();
        // 监听所有网卡；防火墙可能弹窗
        _listener.Prefixes.Add($"http://+:{_cfg.Port}/");
        // 提高大文件上传超时（默认 30s 太短）
        _listener.TimeoutManager.IdleConnection        = TimeSpan.FromMinutes(10);
        _listener.TimeoutManager.HeaderWait            = TimeSpan.FromMinutes(2);
        // EntityBody 超时通过 InputStream 自行控制（net8 不暴露 RequestEntityBody）

        _cts = new CancellationTokenSource();
        _listener.Start();
        _running = true;
        OnState?.Invoke(true);
        LogInfo($"HTTP 服务已启动: http://localhost:{_cfg.Port}  (根目录: {_cfg.RootDir})");

        // 接受循环
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        OnState?.Invoke(false);
        LogInfo("HTTP 服务已停止");
    }

    public void Dispose() => Stop();

    // ==================== 请求处理主循环 ====================

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (HttpListenerException) { break; }       // listener 已停止
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                LogError($"接受连接异常: {ex.Message}");
                continue;
            }
            // 每个请求独立任务，互不阻塞
            _ = Task.Run(() => HandleRequestAsync(ctx));
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var rawPath = "/" + (req.Url?.AbsolutePath.TrimStart('/') ?? "");
            var method  = req.HttpMethod.ToUpperInvariant();

            // 静默 favicon
            if (rawPath == "/favicon.ico")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            // 解析 URL 路径为本地路径
            string localPath;
            try { localPath = ResolveSafePath(rawPath); }
            catch (UnauthorizedAccessException)
            {
                await WriteTextAsync(ctx, 403, "403 Forbidden: 路径越界");
                return;
            }

            switch (method)
            {
                case "GET":    await HandleGetAsync(ctx, rawPath, localPath); break;
                case "POST":   await HandleUploadAsync(ctx, rawPath, localPath); break;
                case "DELETE": await HandleDeleteAsync(ctx, localPath); break;
                default:
                    await WriteTextAsync(ctx, 405, $"405 Method Not Allowed: {method}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogError($"请求处理异常: {ex.Message}");
            try { await WriteTextAsync(ctx, 500, $"500 Internal Server Error: {ex.Message}"); }
            catch { /* ignore */ }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    // ==================== 路由处理 ====================

    private async Task HandleGetAsync(HttpListenerContext ctx, string rawPath, string localPath)
    {
        if (Directory.Exists(localPath))
        {
            await WriteDirectoryListingAsync(ctx, rawPath, localPath);
        }
        else if (File.Exists(localPath))
        {
            await WriteFileAsync(ctx, localPath);
        }
        else
        {
            await WriteNotFoundAsync(ctx, rawPath);
        }
    }

    private async Task HandleDeleteAsync(HttpListenerContext ctx, string localPath)
    {
        if (File.Exists(localPath))
        {
            File.Delete(localPath);
            LogInfo($"删除文件: {localPath}");
            await WriteTextAsync(ctx, 200, "OK");
        }
        else if (Directory.Exists(localPath))
        {
            Directory.Delete(localPath, recursive: true);
            LogInfo($"删除目录: {localPath}");
            await WriteTextAsync(ctx, 200, "OK");
        }
        else
        {
            await WriteTextAsync(ctx, 404, "404 Not Found");
        }
    }

    private async Task HandleUploadAsync(HttpListenerContext ctx, string rawPath, string localPath)
    {
        // rawPath 形如 "/upload"，要提取 ?path= 参数
        string targetDir;
        if (rawPath.Equals("/upload", StringComparison.OrdinalIgnoreCase))
        {
            var queryPath = ctx.Request.QueryString["path"];
            targetDir = string.IsNullOrEmpty(queryPath) ? _cfg.RootDir
                      : ResolveSafePath("/" + queryPath.TrimStart('/'));
        }
        else
        {
            targetDir = localPath;
        }

        if (!Directory.Exists(targetDir))
        {
            await WriteTextAsync(ctx, 404, $"404 Not Found: {targetDir}");
            return;
        }

        var contentType = ctx.Request.ContentType ?? "";
        var boundaryIdx = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (boundaryIdx < 0)
        {
            await WriteTextAsync(ctx, 400, "400 Bad Request: 缺少 multipart boundary");
            return;
        }
        var boundary = "--" + contentType.Substring(boundaryIdx + 9).Trim(' ', '"');

        // 读取请求体（限制大小）
        var maxBytes = _cfg.MaxUploadBytes;
        long totalRead = 0;
        var ms = new MemoryStream();
        var buf = new byte[81920];
        var input = ctx.Request.InputStream;
        while (totalRead < maxBytes)
        {
            var toRead = (int)Math.Min(buf.Length, maxBytes - totalRead);
            var n = await input.ReadAsync(buf, 0, toRead);
            if (n == 0) break;
            ms.Write(buf, 0, n);
            totalRead += n;
        }
        if (totalRead >= maxBytes)
        {
            await WriteTextAsync(ctx, 413, $"413 Payload Too Large (>{maxBytes} bytes)");
            return;
        }
        ms.Position = 0;

        var savedCount = 0;
        var savedNames = new List<string>();
        foreach (var (fileName, data) in MultipartParser.Parse(ms, boundary))
        {
            if (string.IsNullOrEmpty(fileName)) continue;
            // 文件名安全过滤：去除路径分隔符、相对路径
            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeName)) continue;

            var dest = Path.Combine(targetDir, safeName);
            // 防同名覆盖？这里直接覆盖，简化逻辑
            await File.WriteAllBytesAsync(dest, data);
            savedNames.Add(safeName);
            savedCount++;
        }

        var namesText = savedNames.Count > 0 ? string.Join(", ", savedNames) : "无";
        LogInfo($"上传完成: {savedCount} 个文件 → {targetDir} ({namesText})");

        // 浏览器上传完返回到当前目录的 HTML
        var subPath = targetDir.Substring(_cfg.RootDir.Length).Replace('\\', '/').TrimStart('/');
        // 根目录 → "/"（避免拼出 "//"）；子目录 → "/sub/dir"
        var location = string.IsNullOrEmpty(subPath) ? "/" : "/" + subPath;
        ctx.Response.Redirect(location);
    }

    // ==================== 响应生成 ====================

    private async Task WriteDirectoryListingAsync(HttpListenerContext ctx, string rawPath, string localPath)
    {
        var html = BuildDirectoryHtml(rawPath, localPath);
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode      = 200;
        ctx.Response.ContentType     = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        LogInfo($"列出目录: {rawPath}");
    }

    private async Task WriteFileAsync(HttpListenerContext ctx, string localPath)
    {
        var fi = new FileInfo(localPath);
        var total = fi.Length;

        // 解析 Range: bytes=start-end
        long start = 0, end = total - 1;
        var rangeHeader = ctx.Request.Headers["Range"];
        if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var spec = rangeHeader.Substring(6).Split('-');
            if (spec.Length == 2 && long.TryParse(spec[0], out var s))
            {
                start = s;
                if (long.TryParse(spec[1], out var e) && e > 0) end = Math.Min(e, total - 1);
            }
        }

        if (start < 0 || start >= total || end < start)
        {
            await WriteTextAsync(ctx, 416, "416 Range Not Satisfiable");
            return;
        }
        var length = end - start + 1;

        ctx.Response.StatusCode = string.IsNullOrEmpty(rangeHeader) ? 200 : 206;
        ctx.Response.ContentType = GetMimeType(localPath);
        ctx.Response.AddHeader("Accept-Ranges", "bytes");
        ctx.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{total}");
        ctx.Response.ContentLength64 = length;

        // ?download=1 强制下载（弹保存对话框），否则浏览器内嵌预览
        var forceDownload = ctx.Request.QueryString["download"] == "1";
        if (forceDownload)
        {
            // RFC 5987 编码文件名（支持中文）
            var fileName = Path.GetFileName(localPath);
            ctx.Response.AddHeader("Content-Disposition",
                $"attachment; filename=\"{fileName}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
        }

        using var fs = File.OpenRead(localPath);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[81920];
        long remaining = length;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buf.Length, remaining);
            var n = await fs.ReadAsync(buf, 0, toRead);
            if (n == 0) break;
            await ctx.Response.OutputStream.WriteAsync(buf, 0, n);
            remaining -= n;
        }
        LogInfo($"下载文件: {localPath} ({length} bytes, range={rangeHeader ?? "full"})");
    }

    private async Task WriteNotFoundAsync(HttpListenerContext ctx, string path)
    {
        var html = HtmlTemplate.Wrap("404 Not Found",
            $"<p>路径 <code>{HtmlTemplate.Escape(path)}</code> 不存在。</p>" +
            "<p><a href=\"/\">返回根目录</a></p>");
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode      = 404;
        ctx.Response.ContentType     = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteTextAsync(HttpListenerContext ctx, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode      = status;
        ctx.Response.ContentType     = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    // ==================== 路径安全 ====================

    /// <summary>
    /// 解析用户路径为本地物理路径，并校验仍在 RootDir 内。
    /// </summary>
    private string ResolveSafePath(string userPath)
    {
        var decoded = HttpUtility.UrlDecode(userPath) ?? userPath;
        var rel     = decoded.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        // 规范化 root：去掉尾部分隔符（"C:\" 这种盘符根会保留反斜杠）
        var rootFull = Path.GetFullPath(_cfg.RootDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(rootFull))
            rootFull = Path.GetPathRoot(Environment.CurrentDirectory)
                ?? throw new InvalidOperationException("Cannot determine root path");

        var combined = Path.GetFullPath(Path.Combine(rootFull, rel));

        // 校验：必须等于 rootFull 本身（访问根目录），
        // 或以 rootFull + 路径分隔符开头（访问子项）
        if (!combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            LogWarn($"路径越界拦截: url='{userPath}' rel='{rel}' root='{rootFull}' resolved='{combined}'");
            throw new UnauthorizedAccessException("Path traversal");
        }
        return combined;
    }

    // ==================== 目录 HTML ====================

    private string BuildDirectoryHtml(string rawPath, string localPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>📁 {HtmlTemplate.Escape(rawPath)}</title>");
        sb.AppendLine("<style>")
          .AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;max-width:960px;margin:20px auto;padding:0 16px;color:#333}")
          .AppendLine("h2{color:#2C3E50;margin-bottom:8px}")
          .AppendLine(".crumb{color:#7F8C8D;font-size:13px;margin-bottom:16px;padding:8px 12px;background:#ECF0F1;border-radius:4px}")
          .AppendLine(".crumb a{color:#2980B9;text-decoration:none}")
          .AppendLine(".crumb a:hover{text-decoration:underline}")
          .AppendLine("table{width:100%;border-collapse:collapse;background:white;box-shadow:0 1px 3px rgba(0,0,0,0.1);border-radius:4px;overflow:hidden}")
          .AppendLine("th,td{padding:10px 12px;text-align:left;border-bottom:1px solid #ECF0F1}")
          .AppendLine("th{background:#34495E;color:white;font-weight:600;font-size:13px}")
          .AppendLine("tr:hover{background:#F8F9FA}")
          .AppendLine("a{color:#2980B9;text-decoration:none}")
          .AppendLine("a:hover{text-decoration:underline}")
          .AppendLine(".size{color:#7F8C8D;font-family:monospace;font-size:13px}")
          .AppendLine(".actions{text-align:right;white-space:nowrap}")
          .AppendLine(".actions a{margin-left:8px;padding:2px 8px;border-radius:3px;font-size:12px}")
          .AppendLine(".dl{background:#27AE60;color:white!important}.del{background:#E74C3C;color:white!important}")
          .AppendLine(".upload{background:#ECF0F1;padding:16px;border-radius:4px;margin-bottom:16px}")
          .AppendLine(".upload input[type=file]{margin-right:8px}")
          .AppendLine(".upload button{background:#3498DB;color:white;border:none;padding:8px 16px;border-radius:4px;cursor:pointer}")
          .AppendLine(".upload button:hover{background:#2980B9}")
          .AppendLine(".footer{text-align:center;color:#95A5A6;font-size:12px;margin-top:20px}")
          .AppendLine(".delete-form{display:inline;margin:0;padding:0}")
          .AppendLine(".delete-btn{background:#E74C3C;color:white;border:none;padding:2px 8px;border-radius:3px;cursor:pointer;font-size:12px;margin-left:8px}")
          .AppendLine("</style></head><body>");

        // 标题
        sb.AppendLine($"<h2>📁 {HtmlTemplate.Escape(rawPath)}</h2>");

        // 面包屑
        sb.AppendLine("<div class=\"crumb\">");
        sb.AppendLine("<a href=\"/\">🏠 根目录</a>");
        var parts = rawPath.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acc   = "";
        foreach (var p in parts)
        {
            acc += p + "/";
            sb.AppendLine($" / <a href=\"/{HtmlTemplate.Escape(acc.TrimEnd('/'))}\">{HtmlTemplate.Escape(p)}</a>");
        }
        sb.AppendLine("</div>");

        // 上传表单
        sb.AppendLine($"<div class=\"upload\">");
        sb.AppendLine($"<form method=\"post\" action=\"/upload?path={HttpUtility.UrlEncode(rawPath.TrimStart('/'))}\" enctype=\"multipart/form-data\">");
        sb.AppendLine("<input type=\"file\" name=\"file\" multiple required>");
        sb.AppendLine("<button type=\"submit\">⬆ 上传到当前目录</button>");
        sb.AppendLine("<span style=\"color:#7F8C8D;font-size:12px;margin-left:12px\">单文件最大 10GB</span>");
        sb.AppendLine("</form></div>");

        // 目录列表
        var dirInfo = new DirectoryInfo(localPath);
        var dirs    = dirInfo.GetDirectories().OrderBy(d => d.Name).ToArray();
        var files   = dirInfo.GetFiles().OrderBy(f => f.Name).ToArray();

        sb.AppendLine("<table><tr><th>名称</th><th>大小</th><th>修改时间</th><th style=\"text-align:right\">操作</th></tr>");

        if (rawPath != "/")
        {
            var parent = string.IsNullOrEmpty(parts.LastOrDefault()) ? "/" : "/" + string.Join("/", parts.Take(parts.Length - 1));
            if (string.IsNullOrEmpty(parent.Trim('/'))) parent = "/";
            sb.AppendLine($"<tr><td>📁 <a href=\"{HtmlTemplate.Escape(parent)}\">..</a></td><td>--</td><td>--</td><td></td></tr>");
        }

        foreach (var d in dirs)
        {
            var href = (rawPath.TrimEnd('/') + "/" + Uri.EscapeDataString(d.Name));
            sb.AppendLine($"<tr><td>📁 <a href=\"{HtmlTemplate.Escape(href)}\">{HtmlTemplate.Escape(d.Name)}/</a></td>" +
                          $"<td class=\"size\">--</td>" +
                          $"<td class=\"size\">{d.LastWriteTime:yyyy-MM-dd HH:mm}</td>" +
                          $"<td class=\"actions\"></td></tr>");
        }
        foreach (var f in files)
        {
            var href = rawPath.TrimEnd('/') + "/" + Uri.EscapeDataString(f.Name);
            sb.AppendLine($"<tr><td>📄 <a href=\"{HtmlTemplate.Escape(href)}\">{HtmlTemplate.Escape(f.Name)}</a></td>" +
                          $"<td class=\"size\">{FormatSize(f.Length)}</td>" +
                          $"<td class=\"size\">{f.LastWriteTime:yyyy-MM-dd HH:mm}</td>" +
                          $"<td class=\"actions\">" +
                          $"<a class=\"dl\" href=\"{HtmlTemplate.Escape(href)}?download=1\">⬇ 下载</a>" +
                          $"<form class=\"delete-form\" method=\"post\" action=\"/__delete\" onsubmit=\"event.preventDefault();if(confirm('删除 {HtmlTemplate.Escape(f.Name)}？')){{fetch('{HtmlTemplate.Escape(href)}',{{method:'DELETE'}}).then(()=>location.reload())}}\">" +
                          $"<button class=\"delete-btn\" type=\"submit\">🗑 删除</button></form>" +
                          $"</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine($"<div class=\"footer\">BackgroundControl HTTP File Server · {_cfg.GetType().Name} · 无认证开放模式</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ==================== 工具 ====================

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} {units[0]}" : $"{v:0.##} {units[u]}";
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".txt"            => "text/plain; charset=utf-8",
            ".css"            => "text/css; charset=utf-8",
            ".js"             => "application/javascript; charset=utf-8",
            ".json"           => "application/json; charset=utf-8",
            ".png"            => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"            => "image/gif",
            ".svg"            => "image/svg+xml",
            ".pdf"            => "application/pdf",
            ".zip"            => "application/zip",
            ".bin" or ".fw"   => "application/octet-stream",
            _                 => "application/octet-stream",
        };
    }

    private void LogInfo(string msg)  => Log(ToolsLogger.Source.Http, "INFO ", msg);
    private void LogWarn(string msg)  => Log(ToolsLogger.Source.Http, "WARN ", msg);
    private void LogError(string msg) => Log(ToolsLogger.Source.Http, "ERROR", msg);
    private void Log(ToolsLogger.Source src, string level, string msg)
    {
        var prefix = level == "INFO " ? "" : level + " ";
        OnLog?.Invoke($"{prefix}{msg}");
        if      (level == "ERROR") ToolsLogger.Error(src, msg);
        else if (level == "WARN ") ToolsLogger.Warn(src, msg);
        else                       ToolsLogger.Info(src, msg);
    }
}

// ==================== 简易 multipart/form-data 解析器 ====================

internal static class MultipartParser
{
    public static IEnumerable<(string FileName, byte[] Data)> Parse(Stream body, string boundary)
    {
        using var reader = new BinaryReader(body, Encoding.UTF8, leaveOpen: true);
        var boundaryBytes = Encoding.ASCII.GetBytes(boundary);

        // 跳到第一个 boundary
        SkipUntilBoundary(reader, boundaryBytes);

        while (true)
        {
            // 读取 part header
            var headerLines = new List<string>();
            string? line;
            while (!string.IsNullOrEmpty(line = ReadLine(reader)))
            {
                headerLines.Add(line);
            }
            if (headerLines.Count == 0) yield break;

            // 解析 Content-Disposition
            string? fileName = null;
            foreach (var h in headerLines)
            {
                if (h.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase))
                {
                    var fnIdx = h.IndexOf("filename=\"", StringComparison.OrdinalIgnoreCase);
                    if (fnIdx >= 0)
                    {
                        var start = fnIdx + 10;
                        var end   = h.IndexOf('"', start);
                        if (end > start) fileName = h.Substring(start, end - start);
                    }
                }
            }
            if (fileName == null) { SkipUntilBoundary(reader, boundaryBytes); continue; }

            // 读取 part body（直到下一个 boundary）
            var dataMs = new MemoryStream();
            var buf = new byte[81920];
            int boundaryMatchPos = 0;
            bool isLast = false;
            while (true)
            {
                var b = reader.ReadByte();
                if (b < 0) break;
                // 边界匹配
                if (b == boundaryBytes[boundaryMatchPos])
                {
                    boundaryMatchPos++;
                    if (boundaryMatchPos == boundaryBytes.Length)
                    {
                        // 读完整个 boundary 后看是 "--" 结尾还是 "\r\n" 后接下一段
                        var next1 = reader.ReadByte();
                        var next2 = reader.ReadByte();
                        if (next1 == '-' && next2 == '-') isLast = true;
                        // 否则 next1=='\r' && next2=='\n'，进入下一段
                        break;
                    }
                }
                else
                {
                    if (boundaryMatchPos > 0)
                    {
                        // 把已匹配的部分写回（注意：这部分可能是 boundary 开头但不完整）
                        dataMs.Write(boundaryBytes, 0, boundaryMatchPos);
                        boundaryMatchPos = 0;
                    }
                    dataMs.WriteByte((byte)b);
                }
            }

            // 去掉末尾的 \r\n（boundary 前的换行）
            var data = dataMs.ToArray();
            if (data.Length >= 2 && data[^2] == '\r' && data[^1] == '\n')
                data = data.Take(data.Length - 2).ToArray();

            yield return (fileName, data);
            if (isLast) yield break;

            // 下一段
            SkipUntilBoundary(reader, boundaryBytes);
            if (reader.PeekChar() < 0) yield break;
        }
    }

    private static void SkipUntilBoundary(BinaryReader r, byte[] boundary)
    {
        int pos = 0;
        while (true)
        {
            var b = r.ReadByte();
            if (b < 0) return;
            if (b == boundary[pos])
            {
                pos++;
                if (pos == boundary.Length) return;
            }
            else
            {
                pos = 0;
            }
        }
    }

    private static string ReadLine(BinaryReader r)
    {
        var ms = new MemoryStream();
        while (true)
        {
            var b = r.ReadByte();
            if (b < 0) break;
            if (b == '\n')
            {
                var bytes = ms.ToArray();
                if (bytes.Length > 0 && bytes[^1] == '\r') bytes = bytes.Take(bytes.Length - 1).ToArray();
                return Encoding.UTF8.GetString(bytes);
            }
            ms.WriteByte((byte)b);
        }
        return ms.Length == 0 ? "" : Encoding.UTF8.GetString(ms.ToArray());
    }
}

// ==================== HTML 模板辅助 ====================

internal static class HtmlTemplate
{
    public static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    public static string Wrap(string title, string body) =>
        $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{Escape(title)}</title></head><body>{body}</body></html>";
}
