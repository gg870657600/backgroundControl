# Tools Window — Design Doc

## Overview

在 `BackgroundControl` 主窗口顶栏新增 **"工具" 按钮**，点击弹出 `ToolsWindow`，
提供三类本地服务，供 PC1（运行本程序的机器）开放给 PC2（局域网其他机器）使用：

1. **HTTP 文件服务** —— PC2 用浏览器访问 PC1，浏览/下载/上传文件
2. **FTP 服务** —— PC2 用 FTP 客户端或脚本自动化上传/下载
3. **iperf3 打流** —— 测 PC1↔PC2 之间 TCP/UDP 带宽

参考 MobaXterm 的 Tools → Network 菜单设计，**不常驻显示，按需点开**。

## Goals & Non-Goals

### Goals
- 提供 HTTP、FTP、iperf3 三类本地服务，可独立启停
- HTTP 服务自带简易 Web UI，PC2 浏览器开箱即用
- 配置（端口、根目录、凭据）持久化在 `%APPDATA%\BackgroundControl\tools-config.json`
- iperf3.exe 作为嵌入式资源打包到主程序，运行时自动释放到 `%LOCALAPPDATA%`
- 不新增外部 EXE 依赖（除 iperf3 本身）

### Non-Goals
- 不支持 HTTPS（局域网场景，避免证书管理复杂度）
- 不支持 FTP over SSL/FTPS
- 不做用户管理（仅一个 admin 账户 + 可选匿名）
- 不做访问日志持久化（仅显示在 ToolsWindow 状态栏）
- 不做磁盘配额 / 上传限速
- 不做系统托盘（应用关闭即停止所有服务）
- 不动现有的 SSH/Telnet 切换逻辑

## Architecture

### 总体结构

```
MainWindow 顶栏
  └─ [工具 ▼] 按钮
        └─ 弹出 ToolsWindow (非模态，可移动、可关闭)
              ├─ TabControl
              │    ├─ Tab "文件服务"   → HttpFileServerControl.xaml
              │    ├─ Tab "FTP 服务"   → FtpServerControl.xaml
              │    └─ Tab "打流测试"   → IperfControl.xaml
              └─ 底部状态栏：3 个服务的运行/停止状态
```

### 新增文件清单

| 文件 | 作用 | 行数估算 |
|---|---|---|
| `ToolsWindow.xaml(.cs)` | 弹窗主窗口 | 80 |
| `Tools/ToolsConfig.cs` | 配置模型 + 读写 | 100 |
| `Tools/HttpFileServer.cs` | HTTP 服务实现 | 300 |
| `Tools/FtpServerHost.cs` | FTP 服务实现（**自写**，零依赖） | 500 |
| `Tools/IperfRunner.cs` | iperf3 进程封装 | 200 |
| `Tools/IperfLocator.cs` | 释放嵌入的 iperf3.exe | 50 |
| `Tools/BasicAuthHandler.cs` | HTTP Basic Auth 校验 | 30 |
| `Tools/ToolsLogger.cs` | 统一日志（输出到 UI + 内存） | 60 |
| `Views/HttpFileServerControl.xaml(.cs)` | HTTP Tab UI | 180 |
| `Views/FtpServerControl.xaml(.cs)` | FTP Tab UI | 150 |
| `Views/IperfControl.xaml(.cs)` | iperf Tab UI | 250 |
| `tools/iperf3.exe` | 嵌入式资源（实际二进制） | - |

合计 **~1900 行新代码**（含 XAML）。

### 依赖关系

```
HttpFileServer ───┐
FtpServerHost  ───┼──→ ToolsLogger (静态事件)
IperfRunner    ───┘            │
                              ▼
                       ToolsWindow 状态栏
                              │
                              ▼
                    ToolsConfig (JSON 序列化)
```

各服务**完全独立**，可单独启停，单个崩溃不影响其他。

### NuGet 依赖

**无新增**（HTTP 用 `HttpListener`，FTP 自写，iperf3 用进程）。

## Design Details

### 1. ToolsConfig

**位置**：`%APPDATA%\BackgroundControl\tools-config.json`

**模型**：

```csharp
public class ToolsConfig
{
    public HttpConfig Http { get; set; } = new();
    public FtpConfig  Ftp  { get; set; } = new();
    public IperfConfig Iperf { get; set; } = new();

    public static ToolsConfig Load();    // 不存在则返回默认值
    public void Save();                  // 原子写入（写 .tmp 再 rename）
}

public class HttpConfig
{
    public bool   Enabled   { get; set; } = false;
    public int    Port      { get; set; } = 8080;
    public string RootDir   { get; set; } = @"C:\share";
    public string Username  { get; set; } = "admin";
    public string Password  { get; set; } = "Changeme_123";
    public long   MaxUploadBytes { get; set; } = 1L * 1024 * 1024 * 1024; // 1GB
}

public class FtpConfig
{
    public bool   Enabled      { get; set; } = false;
    public int    Port         { get; set; } = 2121;
    public string RootDir      { get; set; } = @"C:\share";
    public int    PassiveStart { get; set; } = 50000;
    public int    PassiveEnd   { get; set; } = 50100;
    public string Username     { get; set; } = "admin";
    public string Password     { get; set; } = "Changeme_123";
    public bool   AllowAnonymous { get; set; } = false;
}

public class IperfConfig
{
    public string IperfExePath { get; set; } = "auto";  // auto = 走 IperfLocator
    public int    DefaultPort  { get; set; } = 5201;
}
```

### 2. HttpFileServer

**核心类**：

```csharp
public class HttpFileServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly HttpConfig _cfg;
    private CancellationTokenSource? _cts;
    private bool _running;

    public event Action<string>? OnLog;     // 日志行
    public event Action<bool>?   OnState;   // 启动/停止状态

    public bool IsRunning => _running;

    public Task StartAsync();
    public void Stop();
    public void Dispose();
}
```

**API 路由**（全部走 `HttpListener`）：

| Method | Path | 行为 |
|---|---|---|
| `GET` | `/` | 列出根目录的 HTML 页面 |
| `GET` | `/subdir/` | 列出子目录 HTML 页面 |
| `GET` | `/file.bin` | 下载文件，**支持 `Range` 头**（206 Partial Content） |
| `POST` | `/upload?path=/subdir/` | `multipart/form-data` 上传到指定子目录 |
| `DELETE` | `/file.bin` | 删除文件（需 Basic Auth） |
| `GET` | `/api/list?path=/` | JSON 格式的目录列表（备用） |
| `GET` | `/favicon.ico` | 返回 204（避免日志噪声） |

**Web UI 模板**（服务端渲染，无 JS 框架）：

```html
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>{breadcrumb}</title>
  <style>
    body { font-family: -apple-system, sans-serif; max-width: 900px; margin: 20px auto; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 8px; text-align: left; border-bottom: 1px solid #eee; }
    .actions { text-align: right; }
    a { color: #0066cc; text-decoration: none; }
    a:hover { text-decoration: underline; }
    .upload-box { background: #f5f5f5; padding: 16px; border-radius: 4px; margin: 20px 0; }
    .crumb { color: #666; margin-bottom: 12px; }
  </style>
</head>
<body>
  <h2>📁 {breadcrumb}</h2>
  <div class="crumb">
    <a href="/">🏠 根目录</a> / {path segments...}
  </div>
  <div class="upload-box">
    <form method="post" action="/upload?path={currentPath}" enctype="multipart/form-data">
      <input type="file" name="file" multiple>
      <button type="submit">⬆ 上传</button>
    </form>
  </div>
  <table>
    <tr><th>名称</th><th>大小</th><th>修改时间</th><th class="actions">操作</th></tr>
    <tr><td>📁 <a href="subdir/">subdir/</a></td><td>--</td><td>2026-06-24 10:30</td><td></td></tr>
    <tr><td>📄 firmware.bin</td><td>125 MB</td><td>2026-06-23 15:22</td><td class="actions"><a href="firmware.bin">⬇ 下载</a></td></tr>
  </table>
  <p style="color: #999; margin-top: 20px;">
    空间: {used} / {total}  ·  BackgroundControl HTTP File Server
  </p>
</body>
</html>
```

**关键设计决策**：
- **路径安全**：所有用户输入路径必须解析后仍在 `RootDir` 内（防 `../` 越权）
  ```csharp
  string ResolveSafePath(string userPath)
  {
      var combined = Path.GetFullPath(Path.Combine(_cfg.RootDir, userPath));
      var rootFull = Path.GetFullPath(_cfg.RootDir) + Path.DirectorySeparatorChar;
      if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
          throw new UnauthorizedAccessException("Path traversal detected");
      return combined;
  }
  ```
- **RootDir 安全警告**：默认 `C:\` 暴露整个系统盘，**强烈建议**首次启动时通过 UI 选择子目录（如 `D:\share`）。HTTP 服务运行时不应以管理员权限运行。
- **Range 支持**：解析 `Range: bytes=0-1023`，用 `FileStream` 的 `Position` + `CopyToAsync` 流式返回
- **上传大小限制**：用 `MultipartReader` 时累加 `ContentLength`，超过 `MaxUploadBytes`（默认 10GB）直接断连
- **大文件不缓存**：HTML 模板、目录列表全部每次重新生成，不放内存
- **认证**：除 `/` 的 GET 列出、文件下载外，POST/DELETE 必须 Basic Auth
- **错误响应**：404 返回简单 HTML 页面，500 返回纯文本错误信息

### 3. FtpServerHost（自写，零依赖）

**核心类**：

```csharp
public class FtpServerHost : IDisposable
{
    private TcpListener? _controlListener;
    private readonly FtpConfig _cfg;
    private CancellationTokenSource? _cts;
    private bool _running;
    private readonly List<FtpClientSession> _sessions = new();

    public event Action<string>? OnLog;
    public event Action<bool>?   OnState;

    public bool IsRunning => _running;

    public Task StartAsync();
    public void Stop();
    public void Dispose();
}

internal class FtpClientSession
{
    public TcpClient Control { get; set; }      // 控制连接
    public TcpListener? DataListener { get; set; }  // 被动模式数据端口
    public NetworkStream? DataStream { get; set; }
    public string CurrentDir { get; set; } = "/";
    public string Username { get; set; } = "";
    public bool Authenticated { get; set; }
    public bool IsAscii { get; set; } = false;
    public long RestartOffset { get; set; } = 0;
    public string PendingRename { get; set; } = "";
}
```

**实现的 FTP 命令**（最小可用集）：

| 命令 | 说明 | 必需 |
|---|---|---|
| `USER` | 用户名 | ✅ |
| `PASS` | 密码 | ✅ |
| `QUIT` | 退出 | ✅ |
| `PWD` / `XPWD` | 当前目录 | ✅ |
| `CWD` / `XCWD` | 切换目录 | ✅ |
| `CDUP` / `XCUP` | 上级目录 | ✅ |
| `LIST` / `NLST` | 目录列表 | ✅ |
| `RETR` | 下载文件 | ✅ |
| `STOR` | 上传文件 | ✅ |
| `DELE` | 删除文件 | ✅ |
| `MKD` / `XMKD` | 建目录 | ✅ |
| `RMD` / `XRMD` | 删目录 | ✅ |
| `TYPE` | 设置 ASCII/Binary | ✅ |
| `PASV` | 被动模式（开数据端口） | ✅ |
| `PORT` | 主动模式（连客户端端口） | ✅ |
| `SIZE` | 文件大小 | ✅ |
| `REST` | 重传偏移（断点续传） | ⭐ 推荐 |
| `NOOP` | 心跳 | ✅ |
| `SYST` | 系统标识 | ✅ |
| `FEAT` | 功能列表 | ✅ |
| `OPTS UTF8 ON` | 启用 UTF-8 | ⭐ 推荐 |

**不支持的命令**：ABOR（中断）、APPE（追加）、STOU（唯一文件名）、MLSD/MLST（机器友好列表）。

**被动模式实现**：
- 从 `PassiveStart~PassiveEnd` 范围内循环取一个端口
- 新建 `TcpListener` 监听该端口
- LIST/RETR/STOR 前 `AcceptTcpClient()` 等待客户端连入
- 传输完关闭 listener

**路径沙箱**（与 HTTP 共用 `ResolveSafePath`）：
```csharp
string ResolveSafePath(string userPath)
{
    var combined = Path.GetFullPath(Path.Combine(_cfg.RootDir, userPath.TrimStart('/')));
    var rootFull = Path.GetFullPath(_cfg.RootDir) + Path.DirectorySeparatorChar;
    if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        throw new UnauthorizedAccessException("Path traversal detected");
    return combined;
}
```

**响应码规范**（RFC 959）：
- `220` 服务就绪
- `331` 用户名 OK，需要密码
- `230` 登录成功
- `530` 未登录
- `550` 文件/目录不存在
- `226` 传输完成
- `150` 打开数据连接
- `425` 无法打开数据连接
- `421` 服务关闭

**数据连接复用规则**（重要）：
- 一次 PASV 只能用于一次数据传输（LIST/RETR/STOR）
- 传输完成后立即关闭数据 socket 和 listener
- 下次传输需重新 PASV

**生命周期**：
- 每个客户端连接一个 `Task.Run(FtpClientSession.ProcessAsync)`
- 主 listener 接受后立即 `AcceptTcpClient()` 等下一个（不阻塞）
- 服务停止时 `_cts.Cancel()` + 关闭所有 session

**配置**（从 `FtpConfig` 读）：
- `cfg.Username` / `cfg.Password`：单用户
- `cfg.AllowAnonymous`：true 时 `USER=anonymous`、`PASS=任意邮箱` 通过

### 4. IperfRunner

**核心类**：

```csharp
public class IperfRunner : IDisposable
{
    private Process? _proc;
    private readonly StringBuilder _stdout = new();
    private ClientOptions? _lastOpts;

    public event Action<string>?  OnLog;          // 原始日志
    public event Action<double>?  OnProgressMbps; // 实时带宽
    public event Action<IperfResult>? OnCompleted; // 测试完成

    public bool IsRunning => _proc is { HasExited: false };

    public Task StartServerAsync(int port, CancellationToken ct);
    public Task<IperfResult> RunClientAsync(string host, int port, ClientOptions opts, CancellationToken ct);
    public void Stop();
    public void Dispose();
}

public class ClientOptions
{
    public int  Seconds  { get; set; } = 10;
    public int  Parallel { get; set; } = 1;
    public bool Udp      { get; set; } = false;
    public bool Reverse  { get; set; } = false;
    public int? Bitrate  { get; set; } = null;  // null = 不限速
}

public class IperfResult
{
    public double SentMbps     { get; set; }
    public double ReceivedMbps { get; set; }
    public double Retransmits  { get; set; }   // TCP
    public double JitterMs     { get; set; }   // UDP
    public double LostPct      { get; set; }   // UDP
}
```

**iperf3.exe 调用**：

- **Server**：`iperf3.exe -s -p {port} -J`
- **Client TCP**：`iperf3.exe -c {host} -p {port} -t {seconds} -P {parallel} -J`
- **Client UDP**：`iperf3.exe -c {host} -p {port} -t {seconds} -P {parallel} -u -b 0 -J`
- **Client Reverse**：`iperf3.exe -c {host} -p {port} -R -J`

**JSON 解析**：
- iperf3 的 `-J` 输出多个 JSON 对象（每个 interval 一个 + 最后的 end）
- 累积 stdout 到 `_stdout`，按 `}{` 切分，最后一个对象是 `end`
- 用 `System.Text.Json` 解析 `end` 对象，提取 `bits_per_second`、`retransmits`/`jitter_ms`/`lost_percent`

**实时进度**（可选）：
- 加 `--interval 1`（iperf3 3.16+），每秒输出一次
- 解析每个 interval 的 `bits_per_second`，触发 `OnProgressMbps`
- IperfControl UI 用 `Dispatcher.Invoke` 更新到图表（简单的 `Canvas` 画折线，不引第三方）

### 5. IperfLocator

**作用**：把内嵌的 `iperf3.exe` 释放到本地磁盘

```csharp
public static class IperfLocator
{
    private static readonly string TargetPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BackgroundControl", "tools", "iperf3.exe");

    public static string GetIperfExePath()
    {
        if (File.Exists(TargetPath)) return TargetPath;

        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("iperf3.exe"))
            ?? throw new FileNotFoundException("iperf3.exe not embedded");

        Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
        using var src = asm.GetManifestResourceStream(resName)!;
        using var dst = File.Create(TargetPath);
        src.CopyTo(dst);
        return TargetPath;
    }
}
```

**csproj 配置**：
```xml
<ItemGroup>
  <EmbeddedResource Include="tools\iperf3.exe" />
</ItemGroup>
```

### 6. BasicAuthHandler

**实现**：

```csharp
public static class BasicAuthHandler
{
    public static bool TryAuthenticate(HttpListenerContext ctx, string username, string password)
    {
        // GET 列表/下载：跳过认证（方便浏览器直接访问）
        if (ctx.Request.HttpMethod == "GET") return true;

        var auth = ctx.Request.Headers["Authorization"];
        if (auth == null || !auth.StartsWith("Basic ")) return Challenge(ctx);

        var encoded = auth.Substring(6);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var parts   = decoded.Split(':', 2);
        if (parts.Length != 2) return Challenge(ctx);

        return parts[0] == username && parts[1] == password;
    }

    private static bool Challenge(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"BackgroundControl\"");
        ctx.Response.Close();
        return false;
    }
}
```

**安全考虑**：HTTP Basic Auth 是**明文传输**，仅限局域网使用。生产场景应上 HTTPS。

### 7. ToolsWindow UI

**XAML 结构**：

```xml
<Window x:Class="BackgroundControl.ToolsWindow"
        Title="工具" Width="700" Height="600"
        WindowStartupLocation="CenterOwner">
  <DockPanel>
    <StatusBar DockPanel.Dock="Bottom">
      <StatusBarItem>
        <TextBlock>
          <Run Text="HTTP: " />
          <Run Text="{Binding HttpStatus}" />
          <Run Text="  FTP: " />
          <Run Text="{Binding FtpStatus}" />
          <Run Text="  iperf: " />
          <Run Text="{Binding IperfStatus}" />
        </TextBlock>
      </StatusBarItem>
    </StatusBar>

    <TabControl Margin="8">
      <TabItem Header="📁 文件服务 (HTTP)">
        <local:HttpFileServerControl />
      </TabItem>
      <TabItem Header="📂 FTP 服务">
        <local:FtpServerControl />
      </TabItem>
      <TabItem Header="📊 打流测试 (iperf3)">
        <local:IperfControl />
      </TabItem>
    </TabControl>
  </DockPanel>
</Window>
```

**HttpFileServerControl** 字段：
- 端口、共享目录、用户名、密码
- 启动/停止按钮
- 状态显示（运行中/已停止）
- 访问 URL 显示（点击复制）
- 实时日志（最近 200 行，自动滚动）

**FtpServerControl** 字段：
- 端口、被动端口范围、根目录、用户名、密码
- 启动/停止按钮
- 状态显示
- 实时日志

**IperfControl** 字段：
- 模式：[Server / Client]
- 协议：[TCP / UDP]
- 目标地址、端口（Client 模式）
- 时长、并发数、限速（Client 模式）
- 启动/停止按钮
- 实时带宽图表（简单的 Canvas 折线）
- 完成结果摘要

### 8. MainWindow 集成

**顶栏增加 "工具" 按钮**：

```xml
<!-- MainWindow.xaml 顶栏 ToolBar -->
<Button Content="🔧 工具" Click="ToolsButton_Click" Margin="4,0" />
```

**事件处理**：

```csharp
private ToolsWindow? _toolsWindow;

private void ToolsButton_Click(object sender, RoutedEventArgs e)
{
    if (_toolsWindow == null || !_toolsWindow.IsVisible)
    {
        _toolsWindow = new ToolsWindow();
        _toolsWindow.Owner = this;
        _toolsWindow.Closed += (s, args) => _toolsWindow = null;
    }
    _toolsWindow.Show();
    _toolsWindow.Activate();
}
```

单例模式：重复点击不会创建新窗口。

## Implementation Plan

按依赖顺序，**4 个阶段**逐步实施：

### 阶段 1：基础设施（~3h）
1. ~~添加 NuGet 包：`FluentFTP.Server`~~ **取消**（FTP 改自写，无新增依赖）
2. 创建 `Tools/ToolsConfig.cs`，含 JSON 序列化
3. 创建 `Tools/ToolsLogger.cs`，含静态事件
4. 创建 `Tools/IperfLocator.cs`，准备 iperf3.exe 资源
5. 在 `MainWindow.xaml` 加 "工具" 按钮，事件处理

**验证**：ToolsWindow 能弹出，三个 Tab 能显示。

### 阶段 2：HTTP 文件服务（~4h）✅ 已完成
1. 创建 `Tools/HttpFileServer.cs`，含路由分发
2. 实现 GET 目录列表（HTML 模板嵌入 C# 字符串）
3. 实现 GET 文件下载（含 Range 支持）
4. 实现 POST multipart 上传
5. 实现 DELETE 删除
6. 实现 `BasicAuthHandler.cs`
7. 创建 `Views/HttpFileServerControl.xaml(.cs)`，绑定 HttpFileServer 事件

**验证**：构建 0 errors。需手动跑：dotnet run → 点 🔧 工具 → 选根目录 → 启动 → 浏览器访问 localhost:8080 → 浏览/下载/上传/删除。

### 阶段 3：FTP 服务（~2h）
1. 创建 `Tools/FtpServerHost.cs`，封装 FluentFTP.Server
2. 实现 `Views/FtpServerControl.xaml(.cs)`

**验证**：PC2 用 FileZilla/wget 访问 PC1，能 put/get 文件。

### 阶段 4：iperf3 打流（~3h）
1. 创建 `Tools/IperfRunner.cs`，含进程管理 + JSON 解析
2. 实现实时进度回调
3. 创建 `Views/IperfControl.xaml(.cs)`，含简单 Canvas 图表

**验证**：PC2 用 `iperf3 -c 192.168.1.10 -p 5201` 测得带宽。

### 阶段 5：打磨与文档（~1h）
1. README.md 增加 "工具" 功能说明
2. README.md 增加 iperf3.exe 来源与许可说明（iperf3 = BSD）
3. 提交代码并推送到 GitHub

**总工作量**：约 13 小时（分 1-2 天完成）。

## Open Questions

- **Q1: iperf3.exe 版本与许可**  
  iperf3 是 BSD 许可，可自由打包。版本选 3.16+（支持 `--interval`）。需要从 https://iperf.fr/ 下载 `iperf3-3.16-win64.zip` 解压取 `iperf3.exe`。

- **Q2: Windows 防火墙**  
  首次启动 HTTP/FTP 监听，Windows Defender 会弹窗。manifest 加 `requestedExecutionLevel` 申请网络权限可减少弹窗次数（但无法消除首次弹窗）。

- **Q3: 大文件上传超时**  
  10GB 上传，HTTP 默认 `Timeout` 可能不够。`HttpListener` 需要手动设置 `TimeoutManager.IdleConnection` 等属性。

- **Q4: 文件名编码**  
  浏览器上传中文文件名要 URL 编码处理；FTP 端用 UTF-8 还是 GBK 由 modem 端决定，需要实测。

- **Q5: 匿名 FTP**  
  某些场景希望"无密码上传"（HFS 风格）。`FtpConfig.AllowAnonymous` 字段保留，但 FluentFTP.Server 是否原生支持匿名需要查文档。

## Success Criteria

完成时满足：

1. ✅ MainWindow 顶栏有 "工具" 按钮，点击弹出 ToolsWindow
2. ✅ HTTP Tab：可启动/停止 HTTP 服务，PC2 浏览器能：
   - 看到根目录的 HTML 页面
   - 点击下载文件，**断点续传**有效（curl 验证）
   - 通过表单上传文件
3. ✅ FTP Tab：可启动/停止 FTP 服务，PC2 用 FileZilla 能登录、上传、下载
4. ✅ iperf Tab：
   - Server 模式：PC2 客户端连入能看到带宽
   - Client 模式：能测 PC1→PC2 带宽，结果显示 Mbps
5. ✅ 配置（端口、目录、凭据）重启后保留
6. ✅ 单文件 publish 后，`tools/iperf3.exe` 首次运行自动释放到 `%LOCALAPPDATA%`
7. ✅ 任何服务崩溃不影响其他服务，UI 状态正确恢复
8. ✅ 设计文档和代码同步推送到 GitHub `main` 分支
