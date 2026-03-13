---
文档类型: RFC 提案
创建日期: 2026-03-13
维护人: X28技术团队
RFC编号: RFC-0002
状态: Draft
---

# RFC-0002 Reactive UI Runtime Window Closure 与 Playable Mod 设计

本文提出 `ReactivePage` runtime window virtualization 的后续 closure target，并给出 3 个 playable mod 设计包。当前已经实现的正式契约见 `docs/reference/reactive_ui_runtime_window_contract.md`；本 RFC 只定义尚未落代码的收口目标与设计要求，不把提案伪装成现状。

## 1 问题与当前缺口

当前仓库已经证明 retained diff 与 virtual window 可以工作，但 closure 还停留在 fixture 层：

- `ReactivePage<TState>` 已经能通过 `UiScene.ApplyReactiveRoot(...)` 走 retained diff，并记录 `UiReactiveUpdateMetrics`：`src/Libraries/Ludots.UI/Reactive/ReactivePage.cs`
- virtual window 声明入口已存在：`src/Libraries/Ludots.UI/Reactive/ReactiveContext.cs`
- camera acceptance panel 已经把大列表切成 stable host + fixed row pool + visible slice，并且手动驱动 runtime refresh：`mods/fixtures/camera/CameraAcceptanceMod/UI/CameraAcceptancePanelController.cs`
- 自动化测试已经证明 scroll 后会触发 `RuntimeWindowChange` 且保持 incremental patch：`src/Tests/ThreeCTests/CameraAcceptanceModTests.cs`

但 generic lifecycle 仍有明确缺口：

- `UIRoot.Update(...)` 不负责 runtime window refresh：`src/Libraries/Ludots.UI/UIRoot.cs`
- `UIRoot.HandleInput(...)` 不负责在 scroll / layout 变化后触发 `RefreshRuntimeDependencies()`：`src/Libraries/Ludots.UI/UIRoot.cs`
- `UiShowcaseMounting.MountReactivePage(...)` 只挂 scene，不声明 mounted reactive page 的 runtime ownership：`mods/UiShowcaseCoreMod/Showcase/UiShowcaseMounting.cs`

这意味着当前能力是“已验证的 fixture contract”，还不是“任何 reactive page 默认拥有的框架能力”。

## 2 提案：以 generic lifecycle integration 作为 closure target

本 RFC 的提案很明确：`ReactivePage` runtime window refresh 的正式 closure target 应该是 generic lifecycle integration，而不是长期保持 caller-owned manual loop。

### 2.1 目标结果

收口完成后，应满足以下结果：

1. 使用 `GetVerticalVirtualWindow(...)` 的 mounted reactive page，在 scroll、viewport resize 或其他 runtime window 变化后，无需 fixture 私有循环也能完成 refresh。
2. generic lifecycle owner 必须是显式、单一、可测试的，不允许多个宿主重复驱动同一 page。
3. `UiReactiveUpdateMetrics`、`UiScene.TryGetVirtualWindow(...)`、`FullRecomposeCount`、`IncrementalPatchCount` 必须继续可见，不能因为生命周期内聚而丢失可观测性。
4. camera acceptance panel 迁移后仍需保留 diagnostics HUD 与自动化断言，避免“框架接管后反而看不见 patch 规模”。

### 2.2 非目标

本 RFC 不做以下事情：

1. 不把 adapter 侧 dirty upload 或 stable identity 同步吸收到 `Ludots.UI` runtime window contract。
2. 不引入第二套 reconciler、shadow tree 或 host-only UI 语义。
3. 不把未实现的 playable mod 设计直接写成 `docs/architecture/` 或 `docs/reference/` 的正式现状。

### 2.3 关闭条件

只有同时满足以下条件，runtime window capability 才能按框架级 closure 关闭：

1. generic lifecycle 已经拥有 runtime refresh，且不依赖 fixture 私有 `MountOrSync(...)`。
2. 至少一个非 camera fixture 的 playable mod 走通同一能力。
3. 自动化测试能验证 scroll / resize / live update 后仍保持 incremental patch，而不是 full remount。
4. 当前 reference contract 与实际代码不再矛盾。

## 3 共享可观测性要求

无论是 inventory、combat log 还是 GM browser，后续 playable mod 都必须共享同一套可观测性要求：

- `UiReactiveUpdateMetrics.Reason` 能区分 `StateChange` 与 `RuntimeWindowChange`
- `VirtualizedWindowCount`、`VirtualizedTotalItems`、`VirtualizedComposedItems` 能证明只 compose visible slice
- `UiScene.TryGetVirtualWindow(...)` 能读到当前 visible range
- `FullRecomposeCount` 与 `IncrementalPatchCount` 能证明没有整页 remount
- 诊断面板或测试日志能把 host id、visible range、patched / reused nodes 暴露出来，而不是只给“看起来更快”的主观描述

## 4 Playable Mod 设计包

### 4.1 Large Inventory / Roster Mod

**建议 mod 名称**：`InventoryRosterPlayableMod`

**玩家流**：

1. 进入 roster 页面，看到 300-1000 条角色 / 物品行。
2. 用关键字、职业、品质或阵营过滤数据集。
3. 滚动到中后段，保持 selection highlight 可见。
4. 只修改单行的数量、装备状态或编队归属。
5. 切换 theme 或调整窗口尺寸，确认 visible range 变化但整页不 remount。

**高频 UI 节点**：

- 过滤输入框内容与激活态 filter chips
- 当前可见行的数量、状态 badge、selection highlight
- 顶部 summary 中的命中数、选中数、当前排序键

**可复用节点**：

- 页面 chrome、列头、过滤工具条、分页 / 总结区
- 行壳体与 spacer 节点
- 与 item 类型无关的通用 row renderer

**如何观察没有整页重组**：

- 滚动后 `UiReactiveUpdateMetrics.Reason` 应转为 `RuntimeWindowChange`
- 修改单个可见行时 `PatchedNodes` 应小于 `ReusedNodes`
- `VirtualizedComposedItems` 应显著小于总行数
- selection 行切换时 `FullRecomposeCount` 不增长

**跨 Web / Raylib / future hosts 的共享 UX 验收**：

- 鼠标滚轮、触控板滚动和键盘分页都能稳定推进 visible range
- 过滤后不会跳回错误位置，selection highlight 不丢失
- theme 切换与 resize 后没有空白窗、重复行或错位 hover
- 相同 viewport 高度下，visible range 与 diagnostics 指标一致

### 4.2 Combat Log / Event Feed Mod

**建议 mod 名称**：`CombatLogFeedPlayableMod`

**玩家流**：

1. 进入实时战斗场景，右侧打开 combat log feed。
2. 保持 feed 跟随最新事件滚动，同时允许玩家手动暂停跟随。
3. 使用事件类型过滤，只显示 damage / heal / crowd control 等子集。
4. 快速触发连续事件，验证只 patch 可见窗口与尾部计数。
5. 回到历史段落查看旧记录，再恢复 live tail。

**高频 UI 节点**：

- 最新可见事件行的文本、数值、来源 / 目标标签
- live tail 开关、未读计数、过滤标签
- visible range 内的 severity / school badge

**可复用节点**：

- feed 外框、筛选栏、分段标题、滚动宿主
- 行模板、时间戳列、spacer 节点

**如何观察没有整页重组**：

- 连续 append 日志时，`PatchedNodes` 主要集中在可见尾部节点和 summary 节点
- 当玩家停在历史段落时，新的 off-screen append 不应导致整页 remount
- `TryGetVirtualWindow(...)` 可确认只移动尾部 window，而非重建所有旧行
- diagnostics 应能显示 live tail on/off、visible range、patched / reused nodes

**跨 Web / Raylib / future hosts 的共享 UX 验收**：

- live tail 行为一致：跟随开启时始终贴尾，关闭时保留人工滚动位置
- 高频事件下没有抖动、跳页或重复渲染旧消息
- 过滤条件切换后，visible range 与 unread 计数能收敛到同一结果
- host 切换不会改变时间戳顺序、颜色编码和 pause/resume 语义

### 4.3 GM / Diagnostics Browser Mod

**建议 mod 名称**：`GmDiagnosticsBrowserMod`

**玩家流**：

1. 打开 GM / diagnostics browser，左侧浏览 section tree，右侧查看列表和详情。
2. 在 entity、camera、navigation、input 等 section 间切换。
3. 调整窗口宽度、切换 theme、展开 / 折叠 diagnostics 子区块。
4. 在后台热更新数据时，前台仍保持当前 section、scroll position 和选中项。
5. 使用搜索或筛选定位某个 entity / system / metric。

**高频 UI 节点**：

- 当前 section 的 value cells、状态 badge、搜索命中高亮
- 顶部 tick / perf counters、active section summary
- 右侧详情面板中的热更新数值

**可复用节点**：

- 左右分栏布局、section header、搜索栏、列表行壳体
- 通用 diagnostics card、折叠面板、spacer 节点

**如何观察没有整页重组**：

- section 切换只应 patch 当前主内容区，不应 remount 整个 browser chrome
- resize / theme change 后 diagnostics shell 继续复用既有节点
- 热更新 value cell 时，`PatchedNodes` 主要出现在当前可见详情区与可见列表行
- `TryGetVirtualWindow(...)` 能反映列表区 visible range，在大 section 下仍只 compose visible slice

**跨 Web / Raylib / future hosts 的共享 UX 验收**：

- 同一 section tree、筛选器和热更新节奏下，visible rows 与选中项一致
- resize 后左右分栏比例、滚动条行为和 hover / focus 状态不漂移
- theme 切换不影响 diagnostics 值的可读性和告警颜色语义
- 搜索命中和展开态在 host 之间保持同一交互语义

## 5 建议交付顺序

1. 先把 runtime window refresh 收束进 generic lifecycle，并补齐通用测试入口。
2. 再把 inventory / roster mod 作为第一个非 camera fixture 的大列表验证面。
3. 接着用 combat log feed 压可见窗口 append 行为。
4. 最后用 GM / diagnostics browser 压多区块、resize、theme 与热数据更新。

## 6 相关文档

- 当前正式契约：见 [../reference/reactive_ui_runtime_window_contract.md](../reference/reactive_ui_runtime_window_contract.md)
- 统一 UI 架构：见 [../architecture/ui_runtime_architecture.md](../architecture/ui_runtime_architecture.md)
