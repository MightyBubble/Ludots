# Order / Navigation / Movement Architecture

## 范围

本文定义 Ludots 中 RTS 风格移动的权威运行时契约。

覆盖内容：

- selector 与 local-order handoff
- order queue、active order 与 nav path 的边界
- 分层移动职责（`查 / 算 / 选 / 走 / Check抵达 / Timeout`）
- 正确的 `NavAgent2D` / `NavGoal2D` 使用契约

主要证据：

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadNetworkLocalOrderSourceSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveOrderBindingSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMovePlanSelectionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveExecutionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveLifecycleSystem.cs`
- `src/Core/Navigation2D/Components/NavGoal2D.cs`
- `src/Core/Navigation2D/Components/NavDesiredVelocity2D.cs`
- `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
- `src/Core/Ludots.Physics2D/Systems/Physics2DToWorldPositionSyncSystem.cs`
- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`

## 单一事实源

以下概念必须分层存储，禁止折叠成同一个字段或同一份运行时结构：

1. `Selection`
- 回答“当前哪些实体被编进同一组”
- 真相来源：`SelectionRuntime` 与选择容器/成员关系

2. `Order queue`
- 回答“哪些 authored command 正在等待或已经激活”
- 真相来源：`OrderBuffer`

3. `Nav plan`
- 回答“当前 active order 对应的执行采样点是什么”
- 真相来源：feature 自己维护的 plan runtime/store，例如 `RoadNavPlanStore`

4. `Nav execution`
- 回答“底层 agent 当前正试图到达哪个即时点目标”
- 真相来源：`NavGoal2D`

5. `Steering output`
- 回答“本帧导航层产出的期望速度/力是什么”
- 真相来源：`NavDesiredVelocity2D` 与 `ForceInput2D`

如果某一层直接写另一层的真相字段，设计就是错的。

## 术语契约

### Authored order waypoint

authored waypoint 属于玩家命令意图。

示例：

- 右键移动目标
- `Shift` 排队的移动目标 1 / 2 / 3
- attack-move 目标

authored waypoint 属于 order layer，不属于 nav runtime。

### Nav path sample

nav path sample 是执行期由寻路规划或路径切片产出的点。

示例：

- 道路曲线采样点
- corridor 拐点采样
- 曲线上的投影起点

nav path sample 属于 nav-plan layer，不属于 order queue。

### Immediate nav goal

immediate nav goal 是当前写入 `NavGoal2D` 的点目标。

它必须始终来自当前 nav plan 的执行选择，而不是在执行开始后由 gameplay movement system 重新直接编写。

## 分层运行时流

```text
SelectionRuntime / local controller
  -> local order source (查)
  -> planner / route compute (算)
  -> active-order binding + plan selection (选)
  -> execution intent -> NavGoal2D (走)
  -> arrival / timeout / refresh (Check抵达 / Timeout)
  -> Navigation2DSteeringSystem2D
  -> Physics2D simulation
  -> Physics2DToWorldPositionSyncSystem
  -> WorldPositionCm
```

### 1. 查：查询与 authored-order 获取

职责：

- 解析 selector owner 与 selection set
- 解析当前 actor set
- 解析本地输入意图
- 只提交 authored order

相关代码：

- `src/Core/Input/Selection/SelectionRuntime.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadNetworkLocalOrderSourceSystem.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

规则：

- 这一层可以决定 actors 与 authored targets
- 这一层不得伪造 nav sample
- 这一层不得写 `NavGoal2D`、`NavDesiredVelocity2D`、`ForceInput2D` 或 `Position2D`

### 2. 算：路线规划与订单展开

职责：

- 把 authored target 转换为 feature 需要的路线
- 在需要时把最终目标与执行采样点分开编码
- 保留 `Immediate` 与 `Queued` 等订单语义

相关代码：

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadMoveOrderExpander.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRoutePlanningService.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteComputeService.cs`

规则：

- 规划层可以把订单 payload 替换成 feature-specific follow order
- 规划层不得推进运行时 waypoint 游标
- 规划层必须保留 authored 最终目的地语义

### 3. 选：active order 绑定与 nav sample 选择

职责：

- 把当前 active order 绑定到 plan store/runtime
- 当 plan 缺失或过期时修复运行时一致性
- 从 plan 中挑选当前执行采样点

相关代码：

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveOrderBindingSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMovePlanSelectionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadMoveRuntimeService.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteSelectionStrategy.cs`

规则：

- 执行游标属于运行时状态，例如 `RoadNavPlanRuntime.CurrentWaypointIndex`
- authored order payload 不能拿来充当执行游标
- 如果 plan storage 缺失或过期，binding 必须先修复一致性，再由 selection 决定是否终止
- timeout refresh 必须同时更新 active-order payload 与绑定 runtime

### 4. 走：把执行意图写入导航层

职责：

- 把 feature-owned execution intent 转译成 core nav 契约
- 写入 `NavGoal2D`
- 让 core nav/physics 产出期望速度与力

相关代码：

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveExecutionSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteWalkStrategy.cs`
- `src/Core/Navigation2D/Components/NavGoal2D.cs`

规则：

- feature system 可以写 `NavGoal2D`
- feature system 不能直接写 `NavDesiredVelocity2D`
- feature system 不能在 nav-follow movement 中直接写 `ForceInput2D`
- feature system 不能直接积分 `Position2D` 或 `WorldPositionCm`

### 5. Check抵达 与 Timeout

职责：

- 基于 authored 最终目标判断是否到达
- 检测停滞与无进展
- 决定 refresh、abandon 或 completion

相关代码：

- `mods/showcases/road_network/RoadNetworkShowcaseMod/Systems/RoadMoveLifecycleSystem.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteArrivalPolicy.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteTimeoutPolicy.cs`
- `mods/showcases/road_network/RoadNetworkShowcaseMod/Gameplay/RoadRouteRefreshService.cs`

规则：

- 抵达判断必须对 authored 最终目标生效，不能只对当前 sample 生效
- timeout 属于 lifecycle policy，不属于 steering output
- 刷新成功时，保留 active order slot，但必须一致性地替换 payload 与 plan runtime
- 刷新失败时，通过 order layer 完成或放弃，不得留下孤儿运行时状态

## 正确的 `NavAgent2D` 使用方式

### gameplay system 可以假设什么

对于 nav-driven mover，gameplay system 可以假设：

- `NavAgent2D` 代表它参与 Navigation2D
- 对于点目标移动，只需要设置 `NavGoal2D`
- `NavDesiredVelocity2D` 是导航层输出，不是输入

### gameplay system 不得做什么

gameplay 与 mod system 不得：

- 把 `NavDesiredVelocity2D` 当成输入来写
- 在正常 nav-follow movement 中直接写 `ForceInput2D`
- 每帧直接改 `Position2D` / `WorldPositionCm` 试图“帮导航追上”
- 在 showcase 代码里保留 feature-local 的睡眠/唤醒 hack

### 睡眠 / 唤醒契约

如果 nav-driven entity 已经有点目标，core nav/physics 基建必须在 physics integration 之前负责唤醒它。

证据：

- `src/Core/Ludots.Physics2D/Systems/Navigation2DSteeringSystem2D.cs`
- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`

这意味着：

- 唤醒属于 core nav/physics 责任
- showcase mod 可以依赖这条契约
- showcase mod 不能自己再维护一套并行唤醒路径

## 正确的订单语义

### Immediate order

immediate order 会按 order rule 替换当前 active order。

移动栈必须立即响应：

- 重绑 active order runtime
- 丢弃陈旧执行意图
- 立刻从新 plan 中重新选择执行点

### Queued order

queued order 是未来的 authored command，不是当前 nav path 的续写采样。

这意味着：

- `Shift +` 右键 1 / 2 / 3 会生成三个 authored order
- 每个 order 之后都可以独立生成自己的 nav plan 与运行时 sample
- 第 2 段失败时，要在 order layer 处理，不能通过篡改第 1 段 nav sample 来兜底

## 参考验收证据

- `src/Tests/GasTests/RoadNetworkShowcaseTests.cs`
- `artifacts/acceptance/road_network_showcase_timeout/battle-report.md`
- `artifacts/acceptance/road_network_showcase_timeout/trace.jsonl`
- `artifacts/acceptance/road_network_showcase_timeout/path.mmd`
