# backgroundControl

A WPF application that uses local AI to issue commands to satellite modems.

Connects to a modem via SSH and auto-switches to a Telnet CLI for executing
device-specific (private) commands while keeping normal Linux commands in the
SSH shell.

## 简介

- SSH 连接调制解调器，自动在 SSH Shell 与 Telnet CLI 之间切换
- 支持自然语言指令（关键词/正则/模糊匹配）
- 集成 WinSCP 文件浏览器（拖拽上传/下载）
- 终端内嵌搜索（Ctrl+F）、清屏、复制日志

克隆后即可直接使用，所有源文件已完整上传到 main 分支。

## 前置条件

- Windows 10/11 + .NET 6 SDK
- Visual Studio 2022 或 `dotnet build`
- 设备可达（PC 能 ping 通调制解调器）

## 构建运行

```bash
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

## 配置说明

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
├── docs/superpowers/specs/              # 设计文档
└── Properties/PublishProfiles/          # 发布配置
```

## 许可

仅供内部使用。
