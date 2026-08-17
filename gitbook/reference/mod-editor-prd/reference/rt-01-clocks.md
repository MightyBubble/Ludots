# rt-01 reference · 时钟系统

> 现状参考。第一性需求见 [rt-01 PRD](../prd/rt-01-clocks.md)；配置说明见 [rt-01 配置说明](../config/rt-01-clocks.md)。

## 1. 现状快照

- 三域：GasClockId{FixedFrame=0, Step=1, EntityLocal=2}；仅前两值映射为引擎全局域（EntityLocal 无全局域映射，转全局域即抛错）；引擎全局域另有 PhysicsStep、NavigationStep。
- 步进策略三态 Auto/Manual/Paused；scalePermille 默认 1000、≥0 无上限；RequestStep 仅 Manual 消费；Auto 每固定 tick 消费步数=千分比累进器（阈值=stepEveryFixedTicks×1000）。
- GasClockSystem 挂 InputCollection 组：每固定 tick 先 Advance(FixedFrame) 再按策略消费 N 个 Step；仅手动步发射 TurnAdvanced 脚本事件。
- 实体变速：属性 `time.scale_permille`；缺 AttributeBuffer 抛错；读值五连校验（存在/有限/整数千分比 |raw−rounded|≤0.001/≥0/≤MaxScalePermille=8000）；本地步进 input=ConsumedGlobalSteps×localScale，阈值 1000 累加 LocalStep。
- tick 换算：FixedHz=20（1 固定 tick=50ms）；stepRateHz=FixedHz÷max(1,stepEveryFixedTicks)，启动期注入订单缓冲等下游。
- 配置 `GAS/clock.json` 两字段显式必填（缺/空串/带空白/非整数均启动失败）；现状 Auto/1。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 三域枚举 | src/Core/Gameplay/GAS/GasClockId.cs:3-8 |
| 全局域映射（EntityLocal 除外） | src/Core/Gameplay/GAS/GasClockDomainIdExtensions.cs:7-12 |
| 引擎全局域枚举 | src/Core/Engine/ClockFoundation.cs:7-12 |
| 步进策略三态与千分比消费 | src/Core/Gameplay/GAS/GasClockStepPolicy.cs:25-88 |
| 千分比累进器 | src/Core/Gameplay/GAS/PermilleStepAccumulator.cs |
| 时钟配置显式必填 | src/Core/Gameplay/GAS/Config/GasClockConfig.cs:36-88 |
| 每固定 tick 推进与 TurnAdvanced | src/Core/Gameplay/GAS/Systems/GasClockSystem.cs:28-49 |
| 实体本地五连校验 | src/Core/Gameplay/GAS/Systems/EntityLocalClockSystem.cs:55-105 |
| 千分比默认与上限 | src/Core/Engine/TimeFlow/TimeFlowService.cs:24-25 |
| time.scale_permille 注册 | src/Core/Gameplay/GAS/TimeAttributeNames.cs:5；src/Core/Engine/GameEngine.cs:784 |
| 时钟系统装配 | src/Core/Engine/GameEngine.cs:1327、1696 |
| stepRateHz 换算 | src/Core/Engine/GameEngine.cs:1440；src/Core/Gameplay/GAS/GasStepRate.cs:7-18 |
| 配置现状 | assets/GAS/clock.json；assets/Engine/clock.json |

**相关文档**：[rt-01 PRD](../prd/rt-01-clocks.md) · [ent-01 reference](ent-01-templates.md)
