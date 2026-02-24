# Pacemaker 时间与步进

Pacemaker 负责把平台侧的帧间隔时间 `dt` 转换为“固定步长”的模拟推进，确保逻辑系统在固定频率下执行，并为表现层提供插值参数。

代码位置：`src/Core/Engine/Pacemaker/IPacemaker.cs`

## 1 职责边界

*   **Pacemaker 只管步进**：决定每帧要推进多少次 FixedStep，以及每次 FixedStep 的步长（`Time.FixedDeltaTime`）。
*   **系统组负责逻辑**：Pacemaker 调用模拟系统组（或 cooperativeSimulation）的 `Step/Update`，不关心具体业务。
*   **表现层插值依赖它**：RealtimePacemaker 计算 `InterpolationAlpha`，供表现层系统把上一帧与当前帧状态做平滑过渡。

## 2 固定步长与插值

RealtimePacemaker 使用累加器 `_accumulator` 累积 `dt`。当累加器达到固定步长 `Time.FixedDeltaTime` 时，推进一次 FixedStep 并扣减累加器，循环直到不足一个固定步长。

插值参数 `InterpolationAlpha` 由累加器与固定步长计算得到，用于表现层插值渲染：

*   `alpha = accumulator / FixedDeltaTime`，范围限制在 0 到 1。
*   alpha 只用于表现层平滑，不应写入决定性状态。

## 3 BudgetFuse 与 CooperativeSimulation

当逻辑系统需要“可切片执行”（避免单帧卡死）时，Pacemaker 支持 `ICooperativeSimulation`：

*   每帧给定 `timeBudgetMs`，在预算内尽可能推进 cooperative simulation。
*   如果一个逻辑步长需要切片次数超过 `maxSlicesPerLogicFrame`，触发 BudgetFuse：
    *   Pacemaker 标记 fused 并停止继续推进；
    *   引擎层会触发 `GameEvents.SimulationBudgetFused` 供上层可观测与处理。

## 4 相关文档

*   表现管线与 Performer 体系：见 [06_presentation_performer.md](06_presentation_performer.md)
*   ConfigPipeline 合并管线：见 [07_config_pipeline.md](07_config_pipeline.md)
