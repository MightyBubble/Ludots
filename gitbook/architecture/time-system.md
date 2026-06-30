# 时间体系

本页定义 Ludots 当前正式时间语义。时间体系只有一条主模拟链路：`GameEngine.Tick()` 通过 Pacemaker 推进全局 FixedStep，TimeFlow 只改变这条链路上的 domain 速率，不引入第二套 scheduler。

## 1 分层

- Pacemaker：决定本帧推进多少次全局 FixedStep。
- TimeFlow：在 domain 层组合暂停与倍率，当前内建 domain 只有 `simulation` 和 `simulation.gas`。
- GAS 离散时钟：`FixedFrame` 是每个 FixedStep，`Step` 是 GAS 逻辑步。
- Entity-local 时钟：`EntityLocalClock.LocalStep` 是单个 entity 自有的 GAS 逻辑时间。

## 2 Entity-local 时间

单体 haste / slow / freeze 使用显式 opt-in：

- entity 必须有 `EntityLocalClock`。
- entity 必须有 `AttributeBuffer`，并且其中必须存在 `time.scale_permille`。
- `time.scale_permille` 由 GAS 属性聚合管线提供，默认 authored 值是 `1000`，`0` 表示冻结，`2000` 表示两倍速。
- `EntityLocalClockSystem` 只消费 `GasClockStepPolicy.LastConsumedSteps`，因此 Auto / Manual / Paused 的全局 Step 语义会自然传递到单体时间。

`GasClockId.EntityLocal` 不是全局 `ClockDomainId`。Effect / timed-tag 使用该时钟时读取 owner/target 的 `EntityLocalClock.LocalStep`；缺少对应组件或属性必须 fail-fast。

## 3 回合语义

没有全局 `Turn` 时钟域。

- 回合时长：使用 `Step`，并把 GAS clock 配成 `Manual`。
- 回合边界：使用 `GameEvents.TurnAdvanced`。
- 一次 `GasClockStepPolicy.RequestStep()` 被消费并推进 `Step` 时，视为一次回合推进。

配置中再声明 `"Turn"` 时钟属于错误输入，loader 必须拒绝。

## 4 Physics / Navigation 与 TimeFlow

`physics2d` / `navigation2d` 不再是 TimeFlow 时间倍率 domain。Physics Hz 和 Navigation cadence 是分辨率 / 执行节奏，不是“让世界更快或更慢”的时间倍率。

全局变速走 `simulation`；GAS 逻辑变速走 `simulation.gas`；单体 GAS 逻辑变速走 `time.scale_permille` + `EntityLocalClock`。

## 5 突发控制器

突发控制器仍运行在唯一 FixedStep 链路上，遮罩子系统执行，不创建第二套调度器。

- `GasController.RunUntilEffectWindowsClosed` 拥有 `SimulationLoopController`，会切到 turn-based 并持续排 FixedStep，直到 GAS runtime idle、阻塞输入、取消或达到上限。
- `Physics2DController.RunForFixedTicks` / `RunUntilSleeping` 只启用 physics Hz 与预算；完成依赖宿主继续 Tick 并调用 `AfterPhysicsFixedTick`。

两者差异是正式契约：GAS 可自驱回合循环，Physics2D 保持 host-driven。

## 6 深度材料

- 仓库深度版：`docs/architecture/time_flow.md`
- Pacemaker 深度版：`docs/architecture/pacemaker.md`
- ADR：`docs/adr/ADR-0004-time-system-entity-local-and-turn-semantics.md`
