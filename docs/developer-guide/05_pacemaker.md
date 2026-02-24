# Pacemaker 时间与步进

本篇只讲 Pacemaker 的职责边界：它如何把平台帧间隔 `dt` 转换为固定步长的模拟推进，以及在预算受限时如何切片执行并触发 BudgetFuse。

代码位置：`src/Core/Engine/Pacemaker/IPacemaker.cs`

## 1 职责边界

Pacemaker 只负责“何时推进模拟”与“推进多少次”，不负责业务逻辑本身。

*   **负责什么**
    *   决定每帧要推进多少次 FixedStep。
    *   推进时使用的步长来自 `Time.FixedDeltaTime`（由引擎在初始化时设置）。
    *   在可切片模式下，按预算驱动 `ICooperativeSimulation`，并在超过切片上限时触发 BudgetFuse。
    *   提供表现层插值用的 `InterpolationAlpha`（RealtimePacemaker）。
*   **不负责什么**
    *   不决定系统的分组与执行顺序（SystemGroup 的编排由 GameEngine 负责）。
    *   不决定“表现系统怎么渲染/怎么插值”，只提供 alpha；插值逻辑由表现系统实现。
    *   不做任何“跨帧决定性状态”的写入；它只驱动 Step/Update 调用。

## 2 固定步长与插值

RealtimePacemaker 使用累加器 `_accumulator` 累积 `dt`。当累加器达到固定步长 `Time.FixedDeltaTime` 时，推进一次 FixedStep 并扣减累加器，循环直到不足一个固定步长。

插值参数 `InterpolationAlpha` 由累加器与固定步长计算得到，用于表现层插值渲染：

*   `alpha = accumulator / FixedDeltaTime`，范围限制在 0 到 1。
*   alpha 只用于表现层平滑，不应写入决定性状态。

## 3 BudgetFuse 与 CooperativeSimulation

当模拟系统需要“可切片执行”（避免单帧卡死）时，Pacemaker 支持 `ICooperativeSimulation`：

*   每帧给定 `timeBudgetMs`，在预算内尽可能推进 cooperative simulation。
*   如果一个逻辑步长需要切片次数超过 `maxSlicesPerLogicFrame`，触发 BudgetFuse：
    *   Pacemaker 标记 fused 并停止继续推进；
    *   引擎层会触发 `GameEvents.SimulationBudgetFused` 供上层可观测与处理。

这个机制的目标不是“加速模拟”，而是把一次 FixedStep 拆成多次 slice，让主线程保持响应。

## 4 与引擎 Tick 的连接方式

引擎每帧调用 `GameEngine.Tick(platformDeltaTime)`：

1.  先把平台侧 `platformDeltaTime` 乘上 `Time.TimeScale`，得到本帧 `dt`。
2.  再调用 `Pacemaker.Update(dt, cooperativeSimulation, budgetMs, sliceLimit)` 推进模拟（FixedStep）。
3.  最后执行表现循环（渲染/UI/动画），这部分每帧都会运行，不依赖是否推进了 FixedStep。

如果需要定位“谁在调用 Pacemaker”，从 `GameEngine.Tick` 追即可。

## 5 常见误区

*   把 `dt` 写入组件并当作决定性状态：`dt` 是平台输入，且为 `float`，不应成为决定性状态的一部分。
*   用 `timeBudgetMs` 试图“控制模拟频率”：预算只影响切片推进是否能在本帧完成，不改变 `Time.FixedDeltaTime` 的目标频率。
*   依赖 BudgetFuse 自动恢复：BudgetFuse 触发后，Pacemaker 会停止推进，需要上层按产品策略处理（提示、降载、重置等）。

## 6 相关文档

*   表现管线与 Performer 体系：见 [06_presentation_performer.md](06_presentation_performer.md)
*   ConfigPipeline 合并管线：见 [07_config_pipeline.md](07_config_pipeline.md)

