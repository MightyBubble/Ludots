# TimeFlow

本文定义 Ludots 当前主线中的 Core TimeFlow 基建，只描述已经落地的 `src/Core` 行为，不包含候选中的 capability Mod、showcase Mod 或 mini entry Mod。

## 1 目标与边界

TimeFlow 的目标是在不引入第二套仿真调度器的前提下，为 `simulation`、`gas`、`physics2d`、`navigation2d` 提供统一的多 domain 时间倍率控制。实现入口位于 `src/Core/Engine/TimeFlow/`，并通过 `src/Core/Engine/GameEngine.cs` 复用现有主循环、`GasClockStepPolicy`、`Physics2DTickPolicy` 与 `Navigation2DTickPolicy`。

当前主线只包含 Core infra：

* `src/Core/Engine/TimeFlow/TimeFlowService.cs`
* `src/Core/Engine/TimeFlow/TimeFlowDomainIds.cs`
* `src/Core/Engine/TimeFlow/TimeFlowToken.cs`
* `src/Core/Scripting/CoreServiceKeys.cs`
* `src/Core/Engine/GameEngine.cs`
* `src/Core/Gameplay/GAS/GasClockStepPolicy.cs`
* `src/Core/Gameplay/GAS/Systems/GasClockSystem.cs`

以下内容不属于本篇 SSOT：

* `mods/capabilities/time/`
* `mods/showcases/time/`
* `mods/showcases/timeflow/`

这些上层收敛结论记录在 `docs/audits/pr92_timeflow_core_mainline_delivery.md` 与 `artifacts/techdebt/2026-04-01-pr92-timeflow-mainline-convergence.md`。

## 2 复用挂靠点

TimeFlow 没有新建平行 runtime，而是显式复用以下既有基础设施：

* Registry: `src/Core/Registry/StringIntRegistry.cs`
  用于把 domain 名称映射为稳定 id。
* Pipeline: `src/Core/Engine/GameEngine.cs`
  在既有 `Tick()` 主循环中应用 Core 内建 domain 倍率。
* System: `src/Core/Gameplay/GAS/Systems/GasClockSystem.cs`
  继续使用原有 GAS 时钟推进器，只把步进语义扩展为“单 fixed tick 可消费多个 step”。
* Policy: `src/Core/Engine/Physics2D/Physics2DTickPolicy.cs` 与 `src/Core/Engine/Navigation2D/Navigation2DTickPolicy.cs`
  通过调整目标 Hz 复用既有物理与导航节拍控制器。

## 3 Domain 与 Token 模型

`TimeFlowService` 维护一个树状 domain 结构，当前内建 domain 为：

* `simulation`
* `simulation.gas`
* `simulation.physics2d`
* `simulation.navigation2d`

每个 domain 由三类值共同决定最终行为：

* `BaseScalePermille`
  domain 自身基准倍率，默认 `1000`，只在 domain 首次注册时确定。
* `Scale token`
  通过 `AcquireScaleToken(...)` 叠加的局部倍率。
* `Pause token`
  通过 `AcquirePauseToken(...)` 施加的暂停信号。

组合规则位于 `src/Core/Engine/TimeFlow/TimeFlowService.cs`：

* `EnsureDomain(...)` 是幂等注册，不会在重复获取 domain 时悄悄改写基准倍率。
* 同一 domain 上多个 `Scale token` 逐个相乘。
* 子 domain 会继续乘上父 domain 的 `EffectiveScalePermille`。
* 父 domain 一旦 paused，子 domain 也会被视为 paused。
* 最终倍率被钳制到 `0..8000 permille`。

## 4 引擎接线

`GameEngine` 在 `InitializeCoreSystems(...)` 中创建 `TimeFlowService`，并将其挂入 `CoreServiceKeys.TimeFlow`：

* `src/Core/Engine/GameEngine.cs`
* `src/Core/Scripting/CoreServiceKeys.cs`

在每次 `Tick()` 开始时，`GameEngine.ApplyBuiltInTimeFlowScales()` 会把当前有效倍率映射到既有系统：

* `simulation`
  写入 `Ludots.Core.Engine.Time.TimeScale`，影响 `GameEngine.Tick()` 的 `dt`。
* `simulation.gas`
  写入 `GasClockStepPolicy.SetScalePermille(...)`，影响 step 消费速率。
* `simulation.physics2d`
  通过 `ScaleRateHz(...)` 写入 `Physics2DTickPolicy.TargetHz`。
* `simulation.navigation2d`
  通过 `ScaleRateHz(...)` 写入 `Navigation2DTickPolicy.TargetHz`。

这里的关键约束是：TimeFlow 只做倍率决策，不直接推动任何第二套 fixed-step loop。

## 5 GAS 步进语义

`GasClockStepPolicy` 现在通过 `ConsumeStepsForThisFixedTick()` 返回当前 fixed tick 应消费的 step 数量，路径如下：

* `src/Core/Gameplay/GAS/GasClockStepPolicy.cs`
* `src/Core/Gameplay/GAS/Systems/GasClockSystem.cs`

语义如下：

* `Paused`
  固定返回 `0`。
* `Manual`
  只消费 `RequestStep(...)` 显式申请的步数，不受自动倍率影响。
* `Auto`
  以 `scalePermille` 为累加器输入，按 `stepEveryFixedTicks * 1000` 为阈值结算本 tick 应推进的 step 数。

这允许 GAS 在单个 fixed tick 中消费多个 step，从而支持快进；也允许在倍率小于 `1000` 时跨 tick 慢速累积。

## 6 可观测性与证据

当前主线的最小验证路径位于：

* `src/Tests/TimeFlowCoreTests/TimeFlowServiceTests.cs`
* `src/Tests/TimeFlowCoreTests/GasClockTimeFlowTests.cs`
* `src/Tests/TimeFlowCoreTests/TimeFlowCoreAcceptanceTests.cs`

确定性 acceptance artifact 位于：

* `artifacts/acceptance/timeflow-core/trace.jsonl`
* `artifacts/acceptance/timeflow-core/battle-report.md`
* `artifacts/acceptance/timeflow-core/path.mmd`

这些证据只覆盖 Core infra。本篇不声明上层 profile、showcase 或 UI 合同已经进入主线。

## 7 相关文档

* GAS 分层架构：见 [gas_layered_architecture.md](gas_layered_architecture.md)
* Pacemaker：见 [pacemaker.md](pacemaker.md)
* Mod 运行时单一事实源：见 [mod_runtime_single_source_of_truth.md](mod_runtime_single_source_of_truth.md)
* PR92 Core 收敛计划：见 [../audits/pr92_timeflow_core_mainline_delivery.md](../audits/pr92_timeflow_core_mainline_delivery.md)
