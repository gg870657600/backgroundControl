# backgroundControl

使用本地 AI 向调制解调器下发指令的 WPF 应用程序。

## 功能

- SSH 连接调制解调器，自动在 SSH Shell 与 Telnet CLI 之间切换
- 自然语言指令（关键词/正则/模糊匹配）
- 集成 WinSCP 文件浏览器（拖拽上传/下载）
- 终端内嵌搜索（Ctrl+F）、清屏、复制日志
- 串口调试
- HTTP / FTP / iperf3 工具

## 前置条件

- Windows 10/11 + .NET 8 SDK
- Visual Studio 2022 或 `dotnet build`
- 设备可达（PC 能 ping 通调制解调器）

## 构建运行

```bash
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

## 测试

项目包含 **147 个测试**，覆盖单元测试和集成测试。

### 一键运行

```bash
python run-tests.py           # 跑测试 + 生成 HTML 报告
python run-tests.py --no-html # 只跑测试，不生成 HTML
```

### 测试分类

| 分类 | 说明 |
| --- | --- |
| 单元测试 | 命令分类、ANSI 剥离、高亮规则、iperf 解析、路径安全、规则匹配/存储、SSH 历史、终端输入、配置/日志等 |
| 集成测试 | SSH 会话、Telnet 连接、HTTP 文件服务、FTP 服务、iperf 性能、串口、嵌入命令 |

集成测试的 SSH/Telnet 连接读取 `%APPDATA%\BackgroundControl\ssh-history.json` 中最新的会话记录，无需硬编码 IP/密码。

### 测试报告

运行 `python run-tests.py` 后自动生成 HTML 报告：
```
BackgroundControl.Tests/TestResults/test-report.html
```

## 配置

默认 Telnet 凭据在 `SessionControl.xaml.cs` 顶部的常量中定义：

| 常量 | 默认值 | 含义 |
| --- | --- | --- |
| `TelnetDefaultUser` | `root` | Telnet 用户名 |
| `TelnetDefaultPassword` | `Changeme_123` | Telnet 密码 |
| `TelnetDefaultPort` | `2323` | Telnet 端口 |
| `EnterTelnetCmd` | `telnet 0 2323` | 进入 Telnet 命令 |

如需修改请直接编辑常量后重新编译。

## 目录结构

```
.
├── App.xaml / App.xaml.cs               # 应用入口
├── MainWindow.xaml(.cs)                 # 主窗口
├── SessionControl.xaml(.cs)             # SSH/Telnet 会话主面板
├── RuleEditorWindow.xaml(.cs)           # 关键字规则编辑
├── LocalCmdControl.xaml(.cs)            # 本地 CMD 终端
├── SerialPortControl.xaml(.cs)          # 串口调试
├── EmbeddedCmdManager.cs                # 嵌入式命令管理
├── Tools/                               # HTTP/FTP/iperf3 工具
├── CommandClassifier.cs                 # 命令分类器
├── AnsiStripper.cs                      # ANSI 转义剥离
├── BackgroundControl.Tests/             # 测试项目（147 个测试）
│   ├── UnitTests/                       # 单元测试
│   ├── IntegrationTests/                # 集成测试
│   └── TestResults/                     # 测试报告输出
├── run-tests.py                         # 一键测试脚本
├── docs/superpowers/specs/              # 设计文档
└── Properties/PublishProfiles/          # 发布配置
```

## 许可

仅供内部使用。
