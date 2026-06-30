# ADR-0004 时间体系：Entity-local 时间域与回合语义收敛

## 1 背景

时间审计确认：Ludots 已有成熟的全局 FixedStep / TimeFlow / GAS Step 链路，但没有单个 entity 的逻辑时间轴。与此同时，全局 `Turn` 时钟域从不推进，却可被 effect / timed-tag 配置引用，形成永不过期的静默错误。

另一个语义异味是 physics2d / navigation2d TimeFlow 倍率被映射到 Hz。Hz 在固定步长下改变的是子步分辨率 / cadence，不是模拟速度。

## 2 决策

采用以下方案：

*   新增 `EntityLocalClock` 组件，记录 `AccumulatorPermille` 与 `LocalStep`。
*   新增 `time.scale_permille` 属性，复用 GAS 属性聚合管线表达 haste / slow / freeze。
*   抽出 `PermilleStepAccumulator`，作为全局 GAS Step 和 entity-local Step 的共同算法。
*   新增 `GasClockId.EntityLocal`，effect / timed-tag / ability exec 读取 owner 或 target 的 `EntityLocalClock.LocalStep`。
*   删除全局 `Turn` 时钟域；回合时长使用 `Step` + `Manual`，回合边界使用 `GameEvents.TurnAdvanced`。
*   移除 physics2d / navigation2d 内建 TimeFlow domain；Hz / cadence 不再被描述为时间倍率。
*   明确 burst 控制器契约：GAS burst 自驱 turn-based loop，Physics2D burst 由 host Tick 驱动。

## 3 备选方案

*   直接给 entity 写入 float dt 缩放：拒绝。dt 是平台输入，不进入决定性状态。
*   保留 `Turn` 并补推进：拒绝。会与 `Step` Manual 形成两个回合真相。
*   用事件自减实现持续 N 回合：拒绝。时长应由离散计数表达，边界反应才使用事件。
*   将 TimeFlow scale 继续映射到 Physics2D Hz：拒绝。该映射不改变物理时间速度，会制造语义错位。
*   让 Physics2DController 也拥有 Pacemaker 并自驱：暂不采用。Physics2D 当前契约是 host-driven burst，避免让物理控制器跨越主 loop 职责。

## 4 影响

*   Core 新增 `EntityLocalClock`、`EntityLocalClockSystem`、`PermilleStepAccumulator`、`GasClockRuntime`。
*   GAS loader 接受 `EntityLocal`，拒绝 `Turn`。
*   `EffectLifetimeSystem`、`TimedTagExpirationSystem`、`AbilityExecSystem` 支持 entity-local clock 读取。
*   `ComponentRegistry` 与 persistence formatter 覆盖 `EntityLocalClock`。
*   文档口径更新到 `gitbook/architecture/time-system.md` 与 `docs/architecture/time_flow.md`。

## 5 后续约束

*   不得复活全局 `Turn` 时钟域。
*   不得把 Physics2D / Navigation2D Hz 文档成 TimeFlow 时间倍率。
*   新增单体时间消费者必须读取 `EntityLocalClock` 或 `time.scale_permille`，不得另造 local time scale。
*   Per-entity 物理时间缩放不是本 ADR 范围；物理 solver 是世界级推进，单体物理变速需要单独设计。
