# Entity Selection Architecture

> **Command-path deprecation (Epic #522):** 正式 gameplay 命令目标集已迁移到 `EntityViewProfile` + `EntityCollectionRoleKind.CommandSource`（见 `RFC-0061`、`EntityViewRuntime`）。`SelectionRuntime` 仍负责 presentation marker、formal selection mirror 与 legacy order snapshot lease；MassNavigation move intake 不再读取 `SelectionContextRuntime`。

## 范围

本文是 Ludots 选择系统的架构 SSOT，覆盖选择存储、视图选择、订单绑定的选择快照，以及相机、面板、覆盖层、调试工具等选择消费方。

目标如下：

- 让选择真相始终落在 ECS 实体与关系上
- 允许任意实体拥有或查看选择状态
- 把输入限定为“变更意图”，而不是选择真相本身
- 让订单、编队、面板与相机消费同一套 viewed-selection 契约
- 禁止把 `64` 之类的硬编码上限写进选择真相

## 单一事实源

正式选择状态存储在容器实体与成员关系实体中。

- 容器实体：
  - `SelectionContainerTag`
  - `SelectionContainerOwner`
  - `SelectionContainerAliasId`
  - `SelectionContainerKindComponent`
  - `SelectionContainerRevision`
  - `SelectionContainerMemberCount`
- 成员关系实体：
  - `SelectionMemberTag`
  - `SelectionMemberContainer`
  - `SelectionMemberTarget`
  - `SelectionMemberOrdinal`
  - `SelectionMemberRoleId`

`SelectionRuntime` 是唯一允许正式读写这套选择真相的运行时 API。

相关代码：

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionMaintenanceSystem.cs`

这意味着选择不是“玩家专属”机制。本地玩家、AI 指挥官、Boss 控制器、回放观察者、调试视图拥有者、订单快照 lease owner 都只是普通实体。

## 容器模型

选择容器以 `(owner entity, alias key)` 为键，并由 `SelectionContainerKind` 标记语义类别。

当前内建 kind：

- `Live`
- `Snapshot`
- `Group`
- `Formation`
- `Derived`
- `CommandBinding`
- `Debug`

当前内建 alias 与 view key：

- 选择 alias：
  - `selection.live.primary`
  - `selection.formation.primary`
  - `selection.command.preview`
  - `selection.command.snapshot`
- 视图 key：
  - `selection.view.primary`
  - `selection.view.secondary`
  - `selection.view.command-preview`
  - `selection.view.formation`
  - `selection.view.debug`

相关代码：

- `src/Core/Input/Selection/SelectionComponents.cs`
- `src/Core/Input/Selection/SelectionRuntime.cs`

## 变更与查询契约

所有选择写入都必须经过 `SelectionRuntime`。

关键操作：

- 创建或解析容器：
  - `TryGetSelectionEntity(...)`
  - `TryGetOrCreateSelectionEntity(...)`
  - `TryGetOrCreateContainer(...)`
- 变更成员：
  - `ReplaceSelection(...)`
  - `AddToSelection(...)`
  - `RemoveFromSelection(...)`
  - `ClearSelection(...)`
- 克隆或快照：
  - `TryCloneSelection(...)`
  - `TryCreateSnapshotLease(...)`
- 绑定视图：
  - `TryBindView(...)`
  - `TryResolveViewContainer(...)`
- 为消费者描述容器或视图：
  - `TryDescribeContainer(...)`
  - `TryDescribeSelection(...)`
  - `TryDescribeView(...)`

消费者侧辅助 API：

- `SelectionContextRuntime.TryGetCurrentPrimary(...)`
- `SelectionContextRuntime.TryGetCurrentContainer(...)`
- `SelectionContextRuntime.CopyCurrentSelection(...)`
- `SelectionContextRuntime.TryDescribeCurrentView(...)`

相关代码：

- `src/Core/Input/Selection/SelectionRuntime.cs`
- `src/Core/Input/Selection/SelectionContextRuntime.cs`
- `src/Core/Input/Selection/SelectionViewDescriptors.cs`

## 选取规则

输入系统只负责产生“选取结果”和可选的“选择变更意图”。点击、框选等 UI acquisition 不再天然等同于正式 selection mutation。

当前选取系统：

- 点击与框选：
  - `src/Core/Input/Selection/CurrentSelectionApplySystem.cs`
- ability 驱动的选择响应：
  - `src/Core/Input/Selection/GasSelectionResponseSystem.cs`
- Tab 目标循环：
  - `mods/CoreInputMod/Systems/TabTargetCycleSystem.cs`

被选资格仍然拆成稳定能力与临时运行时门控两层：

- `SelectionSelectableTag`
- `SelectionSelectableState`
- `SelectionEligibility`

点击与框选先把命中实体写入 `EntityCollectionStore`：

- 默认 acquisition collection：`collection.ui.selection.acquisition`
- collection role：`AcquisitionPreview`
- source kind：`UiAcquisition`
- 配置入口：`SelectionAcquisitionConfig`

`SelectionAcquisitionConfig.CommitToFormalSelection` 决定这次 acquisition 是否继续通过 `SelectionRuntime` 写入正式选择容器。也就是说，UI 框选、hover/query 结果、调试圈选都可以作为 collection 被面板读取，而不必污染正式 selection。

当单位临时不可选时，已有选择不会被自动剔除；自动维护只移除已经死亡的成员。这样可以保持 AI、调试视图、订单快照等状态稳定，不把隐藏策略重写进维护系统。

## 视图选择

viewed selection 与底层存储显式分离。

- 存储真相位于容器与成员关系实体
- viewer entity 把某个 `view key` 绑定到容器
- 消费者从以下服务解析当前 viewed selection：
  - `CoreServiceKeys.SelectionViewViewerEntity`
  - `CoreServiceKeys.SelectionViewKey`

`SelectionViewRuntime` 负责解析当前 viewed selection，`SelectionContextRuntime` 在其之上提供消费者友好的辅助 API。

相关代码：

- `src/Core/Input/Selection/SelectionViewRuntime.cs`
- `src/Core/Input/Selection/SelectionContextRuntime.cs`

消费者不得再创造第二套真相，例如 `SelectedEntity`、`SelectedTag` 或玩家私有缓冲区。

需要展示或查询一组实体，但这组实体并不承担正式 selection 语义时，消费者应使用 `EntityCollectionStore`。collection 是 query/display 状态；只有 `SelectionRuntime` 管理的容器和成员关系实体是正式 selection SSOT。

## 订单与选择快照

订单不再内嵌固定容量的“已选实体数组”。

正式订单侧选择改为：

- `OrderSelectionReference`
- `OrderArgs.Selection`

当订单需要在提交后保留稳定选择快照时，系统会把快照物化为选择容器，并由 lease owner entity 负责持有。

相关代码：

- `src/Core/Gameplay/GAS/Orders/OrderArgs.cs`
- `src/Core/Gameplay/GAS/Orders/OrderQueue.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSelectionLeaseCleanupSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

当前选择到订单的契约如下：

- actor 解析仍由 order actor provider 决定
- selection 负责提供实体集合或稳定快照
- 队列中的订单持有的是容器引用，而不是复制一份定长 payload

对于 RTS 风格移动流程，选择在 authored-order handoff 处结束。

- 右键基于当前选择集提交 authored order
- `Shift +` 右键会继续向 order queue 追加 authored order
- authored waypoint 不是 nav path sample，也不是执行游标

移动侧的 SSOT 拆分见 `docs/architecture/order_navigation_movement.md`。

## 面板、相机与 Mod 消费者

面板、相机 follow target、覆盖层和 showcase mod 必须消费 viewed-selection API 或 descriptor API，而不是直接读底层选择存储。

建议通过以下入口读取：

- `SelectionContextRuntime`
- `SelectionRuntime.TryDescribeView(...)`
- `SelectionRuntime.TryDescribeContainer(...)`

冠军技能沙盒压力测试是这套契约的参考验收 Mod：

- 玩家 live selection view
- 玩家 formation view
- AI target view
- AI formation view
- command snapshot view

相关代码：

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillSandboxRuntime.cs`
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/Runtime/ChampionSkillCastModeToolbarProvider.cs`
- `src/Core/Commands/EntityCommandPanelCommands.cs`

## 编队与控制组语义

编队与控制组语义复用同一套选择容器真相。

- formation 是一种容器 kind 或 alias 选择，而不是第二套成员真相结构
- command preview 与 command snapshot 也是容器或 leased snapshot container
- 多个 viewer 可以同时查看不同容器

因此下面这些状态可以并存，而不需要兼容投影：

- 玩家当前选中的单位
- AI 当前锁定的目标
- Boss 当前标记的受害者
- 调试视图当前查看的编队
- 订单绑定的快照

## 预算与禁止事项

选择真相不允许编码“语义成员上限”。

允许：

- `SelectionRuntimeConfig.MutationApplyBudgetPerFrame` 之类的运行时预算
- UI 边界上的窗口化、截断或虚拟化
- 基于遥测数据的成本控制

禁止：

- `SelectionBuffer.CAPACITY = 64` 之类的硬编码语义上限
- 对正式选择真相做静默截断
- 把 `SelectedEntity` 或 `SelectedTag` 当成权威真相
- 为同一份游戏真相再造一套 mod-local 选择存储
- 把 UI acquisition collection 当成正式 selection truth
- 在 collection consumer 中找不到配置时静默 fallback 到当前 selection

`OrderSpatial.MaxPoints = 64` 是多点空间 payload 的预算，不是选择真相的上限，禁止复用为选择人数限制。

通用 collection/query 基建见 `docs/architecture/entity_collection_query_infrastructure.md`。

## 验收证据

与当前架构对应的验收证据：

- `artifacts/acceptance/champion-skill-sandbox/battle-report.md`
- `artifacts/acceptance/champion-skill-sandbox/trace.jsonl`
- `artifacts/acceptance/champion-skill-sandbox/path.mmd`
- `artifacts/acceptance/champion-skill-stress/battle-report.md`
- `artifacts/acceptance/champion-skill-stress/trace.jsonl`
- `artifacts/acceptance/champion-skill-stress/path.mmd`
- `artifacts/acceptance/champion-skill-stress/screens/timeline.svg`

## 剩余债务

凡是仍然引用 `SelectionBuffer`、`SelectionGroupBuffer`、`SelectedEntity` 或 `SelectedTag`，且又不属于上述正式契约的代码，都是迁移债务，不是正式架构。

当前债务清单记录在：

- `artifacts/techdebt/2026-03-20-selection-container-ssot-redesign.md`
