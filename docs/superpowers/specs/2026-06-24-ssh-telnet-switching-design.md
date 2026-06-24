# SSH / Telnet 环境自动切换 — 设计文档

**日期:** 2026-06-24
**范围:** 修复现有 bug，结构最小变动
**目标文件:** [SessionControl.xaml.cs](file:///e:/chenxi/code/BackgroundControl/SessionControl.xaml.cs)

---

## Overview

项目通过 SSH 连接到设备后，会通过 `telnet 0 2323` 进入设备的私有命令环境，发送 `get-xxx` / `set-xxx` 等私有命令；执行 Linux 系统命令（如 `ls`、`ps`）时则需要先退出 telnet。代码中存在两条几乎重复的"自动切换"路径，且缺少并发保护和硬编码参数散落。本设计在不引入抽象层、不读取设备输出流的前提下，修复这些 bug。

---

## Goals & Non-Goals

### Goals
- 消除 `SendButton_Click` 与 `AutoSwitchByFullCommand` 中的重复切换逻辑
- 加入锁与"切换中"标志，防止快速重复触发导致的状态错乱
- 将 telnet 凭据、端口、超时常量集中到类顶部
- 切换失败时回滚 `_currentEnv`
- 两条命令路径使用统一的命令分类规则

### Non-Goals
- 不引入提示符检测 / 输出流解析（按用户选择）
- 不扩展 `ShellEnvironment` 枚举（用 `_isSwitching` 标志替代中间态）
- 不重写 `MatchCommand` 业务规则
- 不增加配置 UI、单元测试、配置文件
- 不改变现有触发时机（仍由 `IsLinuxCommand` 决定走哪个环境）

---

## 现状问题

### 1. 重复代码
[SendButton_Click（行 942-993）](file:///e:/chenxi/code/BackgroundControl/SessionControl.xaml.cs#L942-L993) 与
[AutoSwitchByFullCommand（行 1206-1239）](file:///e:/chenxi/code/BackgroundControl/SessionControl.xaml.cs#L1206-L1239)
内联的切换序列几乎一字不差。改一处必漏一处。

### 2. 缺少并发控制
`_currentEnv` 直接读写，无锁。快速连点"发送"或终端内连敲命令时，第一次切换还没完成，第二次又读到旧状态，会重复发送 `telnet 0 2323` + `login:root,Changeme_123` 序列。

### 3. 状态机过简
`ShellEnvironment { Unknown, SshShell, TelnetCli }` 没有"切换中"中间态。切换动作执行期间（多个 `await Task.Delay` 跨越）状态处于"既不是 SshShell 也不是 TelnetCli"的灰色期。

### 4. 切换不验证、不回滚
- 写完切换序列后只 `sleep 200/300/100ms` 假设完成
- 设备响应慢/卡顿时，命令会写到错误环境
- 无 try/finally 保护，异常时状态会卡在错误值

### 5. 硬编码散落
- `login:root,Changeme_123` 至少 3 处
- 端口 `2323` 至少 2 处
- 时间常量 100/200/300ms 散落各处

### 6. 两条路径分类标准不一致
- `SendButton_Click` 用 `IsLinuxCommand` 反义 + `IsDirectCommand` fallback
- `AutoSwitchByFullCommand` 只用 `IsLinuxCommand` 反义
- UNKNOWN fallback 时（行 960-965）`isLinuxCmd = !IsDirectCommand(input)` 的反推逻辑只在一条路径生效

### 7. keepalive 启停遗漏
`SendButton_Click` 退出 telnet 时没调 `StopKeepAliveTimer()`，`AutoSwitchByFullCommand` 已正确调用。

---

## Design Details

### A. 集中常量（类顶部字段区新增）

```csharp
// Telnet 凭据与端口
private const string TelnetDefaultUser     = "root";
private const string TelnetDefaultPassword = "Changeme_123";
private const int    TelnetDefaultPort     = 2323;

// Telnet 切换序列片段
private const string EnterTelnetCmd        = "telnet 0 2323";
private const string TelnetLoginFormat     = "login:{0},{1}";

// 切换时序（毫秒）
private const int DelayEnterTelnetMs       = 200;  // 发完 telnet 后等连接建立
private const int DelayAfterLoginMs        = 300;  // 发完 login 后等登录完成
private const int DelayExitTelnetCtrlCMs   = 100;  // Ctrl+C 后等中断生效
private const int DelayAfterExitKeyMs      = 300;  // 'e' 后等退出 telnet
```

### B. 锁与切换中标志（字段区新增）

```csharp
private readonly SemaphoreSlim _switchLock = new(1, 1);
private bool _isSwitching;
```

- `_switchLock` 串行化所有切换动作
- `_isSwitching` 提供可读性，并允许调用方在"切换中"时选择推迟或丢弃

### C. 统一切换方法 `SwitchToAsync(bool toTelnet)`

**签名：**
```csharp
private async Task SwitchToAsync(bool toTelnet);
```

**职责：**
- `await _switchLock.WaitAsync()` 串行化
- 若 `_currentEnv` 已是目标环境，立即释放锁并返回（防重复触发）
- 设 `_isSwitching = true`
- 记录 `previousEnv = _currentEnv`
- try/finally 释放锁 + 清标志
- 异常时回滚 `_currentEnv = previousEnv` 并继续抛出

**切换到 telnet 序列：**
```csharp
_globalShell.WriteLine(EnterTelnetCmd);
await Task.Delay(DelayEnterTelnetMs);
_globalShell.WriteLine(string.Format(
    TelnetLoginFormat, TelnetDefaultUser, TelnetDefaultPassword));
await Task.Delay(DelayAfterLoginMs);
_currentEnv = ShellEnvironment.TelnetCli;
UpdateTelnetStatus(true);
StartKeepAliveTimer();
```

**切回 SSH 序列：**
```csharp
_globalShell.Write("\x03");
await Task.Delay(DelayExitTelnetCtrlCMs);
_globalShell.WriteLine("e");
await Task.Delay(DelayAfterExitKeyMs);
_currentEnv = ShellEnvironment.SshShell;
UpdateTelnetStatus(false);
StopKeepAliveTimer();
```

### D. 两条调用点改造

**`SendButton_Click`** — 删除行 966-989 的内联切换，替换为：
```csharp
if (isLinuxCmd)
    await SwitchToAsync(toTelnet: false);
else
    await SwitchToAsync(toTelnet: true);
```

**`AutoSwitchByFullCommand`** — 删除行 1211-1237 的内联切换，替换为：
```csharp
await SwitchToAsync(toTelnet: !isLinux);
```

### E. 统一分类函数 `ClassifyCommand`（P1）

```csharp
private (bool isLinux, string finalCmd) ClassifyCommand(string input);
```

**行为：**
1. 若 `IsLinuxCommand(input)` 为 true → `finalCmd = input`
2. 否则 `finalCmd = MatchCommand(input)`
3. 若 `finalCmd == "UNKNOWN"`，回退到 `input` 本身
4. 此时重新判断：`isLinux = !IsDirectCommand(input)`（行 960-965 的逻辑）
5. 返回 `(isLinux, finalCmd)`

让两条路径用同一套规则，消除不一致。

### F. 状态机不变

`ShellEnvironment` 保持三态（`Unknown/SshShell/TelnetCli`）。中间态用 `_isSwitching` 标志位表达，避免枚举扩张带来的连锁修改。

---

## Implementation Plan

### 步骤 1：常量与字段
在 SessionControl 类顶部字段区，按 §A、§B 新增约 12 个常量与 2 个实例字段。

### 步骤 2：实现 SwitchToAsync
在 Region "远程命令" 或新增 Region "环境切换" 中实现 `SwitchToAsync` 私有方法。

### 步骤 3：实现 ClassifyCommand
新增 `ClassifyCommand` 私有方法，封装现有 `IsLinuxCommand` + `MatchCommand` + fallback 逻辑。

### 步骤 4：改造 SendButton_Click
- 用 `ClassifyCommand` 替换现有的 `IsLinuxCommand` + `MatchCommand` + fallback 三步
- 用 `SwitchToAsync` 替换内联切换序列

### 步骤 5：改造 AutoSwitchByFullCommand
- 用 `SwitchToAsync` 替换内联切换序列
- 调用前用 `IsLinuxCommand` 判断（保留原行为）

### 步骤 6：编译验证
```bash
dotnet build e:\chenxi\code\BackgroundControl\backgroundControl.csproj
```

### 步骤 7：手动冒烟
- 场景 1：连接后直接发私有命令（应切 telnet）
- 场景 2：telnet 中发 linux 命令（应切回 ssh）
- 场景 3：快速交替点发送 linux / 私有命令（应不重复切换）
- 场景 4：终端内手敲私有命令（应切 telnet）
- 场景 5：连接断开后再连（`_currentEnv` 应回到 `Unknown`）

---

## 改动文件清单

| 文件 | 改动类型 | 估算行数 |
|---|---|---|
| `SessionControl.xaml.cs` | 新增常量/字段/方法 + 改造 2 个调用点 | +60 / -30 |

无其他文件改动。

---

## Open Questions

无。所有用户确认项已锁定：方案 = 修 bug 为主、凭据集中代码顶部、锁+标志不读输出。

---

## Success Criteria

1. `SendButton_Click` 与 `AutoSwitchByFullCommand` 不再包含内联的 `WriteLine("telnet 0 2323")` / `WriteLine("login:...")` 序列
2. 全项目不再出现裸字符串 `"root,Changeme_123"` 与 `2323`（除常量定义处）
3. 快速连点"发送"按钮时，重复触发不会导致重复切换序列
4. 切换过程中 `_isSwitching == true`，可通过日志/调试器观察
5. 切换动作抛异常时，`_currentEnv` 恢复到调用前值，不卡死在错误状态
6. `dotnet build` 通过，无新警告
