# SSH 历史侧边栏

## 数据模型

`Tools/SshHistoryEntry.cs`

```csharp
class SshHistoryEntry {
    string   Ip       // "192.168.1.100"
    int      Port     // 22
    string   Username
    string   Password // DPAPI 加密后的 Base64
    DateTime LastUsed // 排序用，最新在上
}
```

## 持久化

`Tools/SshHistoryManager.cs`

- 文件: `%APPDATA%\BackgroundControl\ssh-history.json` (`List<SshHistoryEntry>`)
- 上限 30 条，新记录插入 `[0]`，超出截断
- 同 IP 覆盖（case-insensitive）
- 密码通过 `ProtectedData.Protect/Unprotect`（DPAPI, `CurrentUser` scope）加解密
- 写入用原子替换（`.tmp` + `File.Replace`）

## 侧边栏 UI

`MainWindow.xaml` Grid 布局:

```
┌──┬──────────┬───────────────────────────┐
│☰ │ 最近连接  │  [+SSH] [串口] [🔧工具]   │
│  │──────────│                            │
│  │ 192...   │      TabControl            │
│  │ 10...    │                            │
│  │ 172...   │                            │
│  └──────────┘                            │
└──┴───────────────────────────────────────┘
```

- Col 0: 16px 切换按钮条（`☰`），点击展开/收起 `SidebarColumn`
- Col 1: `SidebarColumn` 宽度 0↔200，含 `ListBox`
- 列表右侧 context menu：「删除」「清空全部」
- 双击 → 开新标签页 → `ConnectWithCredentials(ip, port, username, password)`
- 展开时自动 `RefreshHistoryList()` 从文件载入

## 自动记录

`SessionControl.xaml.cs` `ConnectSshWithPassword()` 成功后调用:

```csharp
SshHistoryManager.RecordConnection(targetIp, port, username, password);
```

密码用 `plain` 传入，Manager 内部加密。

## 文件

| 文件 | 操作 |
|------|------|
| `Tools\SshHistoryEntry.cs` | 新增 |
| `Tools\SshHistoryManager.cs` | 新增 |
| `MainWindow.xaml` | Grid 分三列 + 侧边栏 |
| `MainWindow.xaml.cs` | 切换/双击/右键删除 |
| `SessionControl.xaml.cs` | 新增 `ConnectWithCredentials()` + 成功后记录 |
| `backgroundControl.csproj` | 添加 `System.Security.Cryptography.ProtectedData` 10.0.9 |
