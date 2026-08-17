# infra-01 配置说明 · 引擎与物理配置

> 配置写法与行为。第一性需求见 [infra-01 PRD](../prd/infra-01-engine-physics.md)；编辑器需求见 [UXD](../uxd/infra-01-engine-physics.md)；现状见 [reference](../reference/infra-01-engine-physics.md)。

## 1. 示例配置

引擎真实四件（`assets/Engine/clock.json`、`assets/Physics2D/*.json`，clock/solver 为现状节选）：

```json
{ "FixedHz": 20 }
```

```json
{ "PhysicsHz": 60, "MaxStepsPerFixedTick": 8 }
```

```json
{
  "SolverIterations": 12,
  "PositionCorrectionPercentage": 1.0,
  "SleepTimeSeconds": 4.0,
  "MaxCollisionPairs": 4096
}
```

```json
{ "kinematicBodyCapacity": 4096, "contactEventQueueCapacity": 4096, "contactEventEmitterLayers": [] }
```

带宽相声明的物理时钟（教学骨架，合成）：

```json
{
  "PhysicsHz": 60,
  "MaxStepsPerFixedTick": 8,
  "broadphase": { "Strategy": "SortAndSweep", "CellSizeCm": 100 }
}
```

注意：**代码缺省 ≠ 实配**。引擎时钟代码默认与实配不同（见 D2）；物理时钟代码默认 15、实配 60。排障先读实际文件，勿信缺省。

## 2. 字段与行为

| 文件 | 字段 | 这样配会产生什么效果 |
|---|---|---|
| Engine/clock.json | `FixedHz` | 固定仿真步频；校验 ≥ 1；驱动 FixedDeltaTime 与 stepRateHz |
| Physics2D/clock.json | `PhysicsHz` | 物理步频；0 关闭物理步 |
| Physics2D/clock.json | `MaxStepsPerFixedTick` | 单固定步内最大补步数（≥1），封顶追帧成本 |
| Physics2D/clock.json | `broadphase.Strategy` | SortAndSweep / UniformGrid 封闭枚举 |
| Physics2D/clock.json | `broadphase.CellSizeCm` | UniformGrid 网格尺寸，≥ 1 |
| Physics2D/solver.json | `SolverIterations` | 求解迭代数（≥1）；越大越稳越贵 |
| Physics2D/solver.json | `PositionCorrectionPercentage` | 位置修正强度，[0,1] |
| Physics2D/solver.json | `SleepTimeSeconds` | 休眠阈值（≥0） |
| Physics2D/solver.json | `MaxCollisionPairs` | 碰撞对上限 |
| Physics2D/kinematic.json | `kinematicBodyCapacity` | 运动学刚体容量，必显式（≥1） |
| Physics2D/kinematic.json | `contactEventQueueCapacity` | 接触事件队列容量，必显式（≥1） |
| Physics2D/kinematic.json | `contactEventEmitterLayers` | 发射接触事件的层白名单；空 = 无层发射 |

solver.json 另有摩擦/弹性/阻尼等默认材料参数（见 reference 锚点）。

## 3. 文件结构

四个 DeepObject 单例文件：`assets/Engine/clock.json`、`assets/Physics2D/clock.json`、`assets/Physics2D/solver.json`、`assets/Physics2D/kinematic.json`（DeepObject 合并：mod 只写要改的字段）。

## 4. 运行时加载效果

引擎初始化早期读取并校验；固定时钟供全局仿真调度，物理参数供物理系统初始化。**生效级别：重启**（时钟与容量变更重启生效）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| FixedHz < 1 / PhysicsHz < 0 / MaxStepsPerFixedTick < 1 | 启动失败，指明字段 |
| SolverIterations < 1 / 修正比出 [0,1] / SleepTimeSeconds < 0 | 启动失败 |
| 运动学三字段缺失或 < 1 | 启动失败（无默认注入） |
| 白名单引用未配置的层 | 校验失败 |
| 容量不足（运行期） | 报错 |

## 6. 实例

- `assets/Engine/clock.json`、`assets/Physics2D/clock.json`、`solver.json`、`kinematic.json`（引擎真实实配）

**相关文档**：[infra-01 PRD](../prd/infra-01-engine-physics.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
