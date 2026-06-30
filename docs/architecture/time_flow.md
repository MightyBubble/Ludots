# Time Flow

`gitbook/architecture/time-system.md` 是正式口径。本页是仓库深度材料，记录当前实现挂靠点、语义边界和验证路径。

TimeFlow 提供 domain-level 时间缩放，但不引入第二套 simulation scheduler。所有推进仍由唯一的 `GameEngine.Tick()` 和 Pacemaker 固定步长链路驱动。

## 1 内建 Domain

| Domain | 常量 | Owner | 语义 |
|---|---|---|---|
| `simulation` | `TimeFlowDomainIds.Simulation` | Core main loop | 根模拟倍率 / 暂停 |
| `simulation.gas` | `TimeFlowDomainIds.Gas` | `GasClockStepPolicy` | GAS Step 消费速率 |

不再有 `simulation.physics2d` 或 `simulation.navigation2d` 内建 TimeFlow domain。Physics / navigation 的 Hz 与 cadence 是分辨率或执行节奏，不承诺时间倍率；单独缩放它们不会被解释为“物理世界变快 / 变慢”。

## 2 GAS 全局时钟

GAS 使用 `GasClockStepPolicy` 消费全局 Step：

- `Auto`：按 `StepEveryFixedTicks` 和 `simulation.gas` 有效倍率累加。
- `Manual`：只消费 `RequestStep()` 的 pending step。
- `Paused`：不消费 step。

`PermilleStepAccumulator` 是全局 Step 与 entity-local Step 的共同算法 SSOT。

## 3 Entity-local 时间

单体逻辑时间由 `EntityLocalClock` 承载：

- 组件：`src/Core/Gameplay/GAS/Components/EntityLocalClock.cs`
- 系统：`src/Core/Gameplay/GAS/Systems/EntityLocalClockSystem.cs`
- 属性名：`time.scale_permille`
- 时钟 ID：`GasClockId.EntityLocal`

数据流：

```text
GAS clock config + TimeFlow simulation.gas
  -> GasClockStepPolicy.LastConsumedSteps
  -> AttributeBuffer.time.scale_permille
  -> EntityLocalClockSystem
  -> EntityLocalClock.LocalStep
  -> EffectLifetimeSystem / TimedTagExpirationSystem / AbilityExecSystem
```

`EntityLocalClockSystem` 只推进显式 opt-in 的 entity。缺少 `time.scale_permille`、非整数 permille、负数、非有限值都 fail-fast。`EntityLocal` 不是全局 `ClockDomainId`，调用 `GasClockId.EntityLocal.ToDomainId()` 会失败。

## 4 回合语义

全局 `Turn` 时钟域已删除。回合拆成两个正交概念：

- 持续 N 回合：使用 `Step`，并把 GAS clock 配成 `Manual`。
- 回合边界反应：订阅 `GameEvents.TurnAdvanced`。

`GasClockSystem` 在消费 manual Step 后推进 `ClockDomainId.Step`，并触发 `TurnAdvanced`。配置中声明 `"Turn"` 时钟会被 loader 拒绝，避免旧路径永不过期。

## 5 Physics / Navigation 边界

`Physics2DTickPolicy.TargetHz` 和 navigation cadence 控制子步分辨率 / 执行节奏，不是 TimeFlow 时间倍率。全局变速使用 `simulation`；GAS 变速使用 `simulation.gas`；单体 GAS 变速使用 `EntityLocalClock`。

这一约束消除两个真相：不会再把 TimeFlow scale 映射成 physics target Hz，也不会把 physics Hz 文档成时间倍率。

## 6 验证

- TimeFlow domain / Turn 删除：`src/Tests/TimeFlowCoreTests/`
- Entity-local 时钟：`src/Tests/GasTests/EntityLocalClockTests.cs`
- Loader fail-fast：`src/Tests/GasTests/*FailFastTests.cs`
- 存档覆盖：`src/Tests/PersistenceTests/ArchPersistenceCharacterizationTests.cs`
- Controller burst 契约：`src/Tests/GasTests/BurstControllerContractTests.cs`
