# 规则编辑器改进 — Design Doc

## Overview
重写规则编辑器窗口，解决无法复制行、插入行、搜索过滤、自动排序的问题。

## Goals
- 搜索框实时过滤规则列表（匹配关键词或命令）
- 行首按钮：⊕复制行、↑插入行、✕删除行
- 保存时自动按 Command 字母排序，相似规则自动聚拢
- 窗口尺寸从 700×500 扩大到 800×600，容纳新布局
- 按钮名从"保存"改为"保存并排序"，明确行为

## Non-Goals
- 不做拖拽排序（用户已确认跳过）
- 不修改 rules.json 格式或存储路径
- 不涉及匹配算法改动

## Design Details
- **搜索过滤**：`CollectionViewSource.GetDefaultView(Rules)` + `Filter` 委托，匹配 `Keywords.ToLowerInvariant()` 和 `Command.ToLowerInvariant().Contains(filterText)`
- **行操作按钮**：`DataGridTemplateColumn` 内嵌三个 `Button`，通过 `DataContext` 获取当前 `RuleItem`，操作底层的 `ObservableCollection<RuleItem>`
- **保存排序**：`Rules.OrderBy(r => r.Command).ToList()` 覆盖原集合再关闭窗口
- **搜索框水印**：用 `Style.Triggers` 在失焦时显示灰色提示文字

## Implementation Plan
1. 重写 `RuleEditorWindow.xaml` — 三行布局（标题→搜索框→DataGrid→按钮）
2. 重写 `RuleEditorWindow.xaml.cs` — 过滤、复制/插入/删除、保存排序

## Success Criteria
- 搜索框输入关键词实时过滤规则列表
- 点击 ⊕ 在当前行下方克隆一行
- 点击 ↑ 在当前行下方插入空行
- 点击 ✕ 删除当前行
- 保存后 rules.json 中规则按 Command 字母排序
