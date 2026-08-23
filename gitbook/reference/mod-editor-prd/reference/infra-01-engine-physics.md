# infra-01 reference · 引擎与物理配置

> 现状参考。第一性需求见 [infra-01 PRD](../prd/infra-01-engine-physics.md)；配置说明见 [infra-01 配置说明](../config/infra-01-engine-physics.md)。

## 1. 现状快照

- Engine/clock.json：实配 `{"FixedHz":20}`；代码缺省 50、校验 ≥1（缺省与实配不一致，D2）；消费 FixedDeltaTime + stepRateHz。
- Physics2D/clock.json：实配 PhysicsHz=60、MaxStepsPerFixedTick=8；代码缺省 PhysicsHz=15；broadphase 可配 Strategy（SortAndSweep 默认 / UniformGrid）与 CellSizeCm≥1；PhysicsHz 校验 ≥0、补步 ≥1。
- Physics2D/solver.json：实配 SolverIterations=12、PositionCorrectionPercentage=1.0、SleepTimeSeconds=4.0、MaxCollisionPairs=4096（代码缺省迭代 6、修正 0.4）；另有摩擦/弹性/阻尼等材料默认。
- Physics2D/kinematic.json：实配 kinematicBodyCapacity=4096、contactEventQueueCapacity=4096、contactEventEmitterLayers=[]；三字段必显式（无默认注入），层白名单校验 + 容量不足运行期报错。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 引擎时钟（缺省 50、校验 ≥1） | src/Core/Engine/EngineClockConfig.cs:10,46-48 |
| 物理时钟与宽相 | src/Core/Engine/Physics2D/Physics2DClockConfig.cs:15-81 |
| 求解器（缺省 6/0.4、区间校验） | src/Core/Engine/Physics2D/Physics2DClockConfig.cs:84-140 |
| 运动学（必显式三字段） | src/Core/Engine/Physics2D/Physics2DKinematicConfig.cs:32 |
| 实配资产 | assets/Engine/clock.json、assets/Physics2D/clock.json、solver.json、kinematic.json |

**相关文档**：[infra-01 PRD](../prd/infra-01-engine-physics.md) · [infra-02 reference](infra-02-navigation.md)
