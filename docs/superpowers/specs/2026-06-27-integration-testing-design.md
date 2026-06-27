# BackgroundControl 集成测试方案设计

## Overview

本方案旨在补齐 BackgroundControl 项目当前约 70% 的测试覆盖缺口，以集成测试（拉起真实服务/进程）为主，覆盖 SSH/Telnet 会话管理、HTTP/FTP 文件操作、iperf 网络测试、串口通信、嵌入 CMD 等核心功能。

## Goals & Non-Goals

### Goals
- SSH 连接到默认设备，测试环境切换（SSH↔Telnet）和命令路由
- HTTP 文件服务器的上传/下载/Range/Delete 全流程
- FTP 服务器的登录/上传/下载/删除/目录操作/多会话
- iperf3 进程管理（TCP/UDP/并行流/带宽限制）及图表性能验证
- 串口通信（com0com 虚拟串口回环测试）
- 嵌入 CMD 进程的启停与命令执行

### Non-Goals
- 不覆盖 WPF UI 控件的自动化测试（如点击按钮、菜单导航）
- 不覆盖第三方库（SSH.NET、WinSCP）的内部逻辑
- 不覆盖高可用或集群场景

## Architecture

### 项目结构

所有新增集成测试放在现有 `BackgroundControl.Tests` 项目中，通过 `[Trait("Category", "Integration")]` 与单元测试区分。

```
BackgroundControl.Tests/
├── UnitTests/                  ← 原有 104 条单元测试
├── IntegrationTests/           ← 新增
│   ├── Fixtures/               ← 共享资源
│   │   ├── HttpServerFixture.cs
│   │   ├── FtpServerFixture.cs
│   │   ├── IperfFixture.cs
│   │   ├── SshFixture.cs
│   │   ├── SerialFixture.cs
│   │   └── TempDirFixture.cs
│   ├── HttpTests/
│   │   └── HttpFileServerTests.cs   ← 追加
│   ├── FtpTests/
│   │   └── FtpServerTests.cs        ← 追加
│   ├── IperfTests/
│   │   ├── IperfRunnerTests.cs      ← 追加
│   │   └── IperfPerformanceTests.cs ← 新建
│   ├── SerialTests/
│   │   └── SerialPortTests.cs       ← 新建
│   └── ...
```

### Fixture 架构

每个 Fixture 实现 `IDisposable`，通过 xUnit 的 `IClassFixture<T>` 注入测试类：

```
Fixture 生命周期：
  构造函数 → 分配资源（端口/临时目录/进程/串口）
  测试执行 → 使用资源
  Dispose  → 释放资源（Kill 进程/删除文件/卸载串口）
```

## Design Details

### 1. SSH 会话 & 环境切换

**SshFixture.cs**
- 读取默认设备 IP（从 `MainWindow` 配置来源）
- 创建 `SshClient` 连接
- 通过 `ShellStreamAdapter` 暴露 `IShellStream`
- `SshClient.KeepAliveInterval` = 30s

**测试列表：**
- 连接到默认设备并验证 `IsConnected`
- SSH→Telnet 环境切换：发送 `telnet 0 2323`，等待 Telnet 提示符
- Telnet→SSH 切换：发送 `Ctrl+C`，验证回到 SSH shell
- Linux 命令自动走 SSH 通道
- 设备命令（get-/set- 前缀）自动切换到 Telnet 环境
- Keep-alive 30s 内不掉线
- 断开后重连恢复

### 2. HTTP 文件操作

**HttpServerFixture.cs**
- 在临时目录上启动 `HttpFileServer`（随机端口）
- 提供 `BaseUrl` 和 `RootPath`
- 使用 `HttpClient` 作为测试客户端

**测试列表：**
- POST multipart 上传文件，验证写入磁盘
- GET 下载文件，验证内容一致
- GET 带 `Range` header，验证 206 Partial Content
- DELETE 删除文件，验证 200 + 磁盘移除
- DELETE 不存在的文件，验证 404
- 路径穿越 `../../../` 请求，验证被拦截（403/404）
- 10MB+ 大文件上传，验证完整性
- 10 并发请求同时下载，验证服务稳定

### 3. FTP 文件操作

**FtpServerFixture.cs**
- 在临时目录上启动 `FtpServerHost`（随机端口）
- 使用 `System.Net.Sockets.TcpClient` 发送原始 FTP 命令

**测试列表：**
- USER anonymous / PASS anonymous → 230 登录成功
- STOR 上传文件，验证文件已写入
- RETR 下载文件，验证内容一致
- DELE 删除文件，验证 250 + 磁盘移除
- MKD 创建目录 / RMD 删除目录
- RNFR / RNTO 重命名文件
- LIST / NLST 列出目录
- PASV 被动模式数据传输
- TYPE I 二进制模式 / TYPE A ASCII 模式
- 两个客户端同时连接

### 4. iperf 网络测试

**IperfFixture.cs**
- 启动 `iperf3 -s -D`（后台 server 模式，随机端口）
- 提供 `IperfRunner` 作为 client

**追加到 IperfRunnerTests.cs：**
- TCP 连本机 server，验证间隔数据非空
- UDP 模式 `-u`，验证 jitter/loss 字段解析
- 多并行流 `-P 5`，验证数据不丢失
- 带宽限制 `-b 10M`，验证结果接近限制值
- 服务端模式：本机起 server，另一实例当 client
- 进程异常退出：Kill iperf3，验证 `IperfRunner` 检测到退出事件
- 超时断开：设置短 duration，验证正常结束

**新建 IperfPerformanceTests.cs：**
- 模拟 100/1000/5000 数据点调用 `UpdateChart()`
- 测量渲染耗时（阈值为 100ms）
- 连续 60s 每秒追加 1 个点，监控内存增长
- 10ms 间隔高频更新，验证 UI 响应不卡死

### 5. 串口通信

**SerialFixture.cs**
- 安装 com0com 虚拟串口对（COM3↔COM4）
- 提供两个 `SerialPort` 实例

**新建 SerialPortTests.cs：**
- 打开串口并验证 `IsOpen`
- COM3 发送数据 → COM4 接收，验证内容一致
- 不同波特率（9600/19200/115200）验证配置生效
- 异步读写并发，验证无数据竞争
- 关闭一端后重新打开，验证恢复

### 6. 嵌入 CMD

**新建 EmbeddedCmdTests.cs：**
- 启动 `EmbeddedCmdManager`
- 验证进程已启动（`GetProcess()` 非 null）
- 发送命令 `echo hello`，验证输出包含 "hello"
- 停止后验证进程已退出
- 停止后再重新启动

## Implementation Plan

1. 创建 Fixture 目录和基础 Fixture 基类
2. `TempDirFixture` — 临时目录管理
3. `HttpServerFixture` + HTTP 测试追加
4. `FtpServerFixture` + FTP 测试追加
5. `IperfFixture` + Iperf 测试追加 + 性能测试
6. `SshFixture` + SSH 会话测试
7. `SerialFixture` + 串口测试
8. `EmbeddedCmdTests`
9. 全局 `runsettings` 配置默认设备 IP、com0com 路径等参数
10. 全量运行验证

## Open Questions

- com0com 是否需要预先安装？还是在测试中自动下载安装？
- SSH 默认设备 IP 的配置来源：`appsettings` 文件还是环境变量？
- iperf 性能测试是否需要在 CI 中排除（依赖真实计时）？

## Success Criteria

- 所有集成测试通过率 100%
- 新增测试数 ≥ 50 条
- SSH/Telnet 环境切换测试覆盖正常切换和异常恢复两条路径
- HTTP/FTP 覆盖所有 CRUD 操作
- iperf 图表 5000 点渲染耗时 < 100ms
- `dotnet test --filter "Category=Integration"` 可一键运行
