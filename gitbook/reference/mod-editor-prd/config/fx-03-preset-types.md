# fx-06 配置说明 · Preset 类型系统

> 配置写法与行为。第一性需求见 [fx-05 PRD](../prd/fx-03-preset-types.md)；编辑器需求见 [UXD](../uxd/fx-03-preset-types.md)；现状见 [reference](../reference/fx-03-preset-types.md)。

## 1. 示例配置

引擎默认 `assets/GAS/preset_types.json`（真实）最简原型一条；图处理器原型见 DeployConsumeSource 行：

```json
[ { "id": "InstantDamage", "components": ["ModifierParams"], "activePhases": ["OnApply"],
   "allowedLifetimes": ["Instant"],
   "defaultPhaseHandlers": { "OnApply": { "type": "builtin", "id": "ApplyModifiers" } } } ]
```

## 2. 字段与十六种内建 preset

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 原型名，效果模板 presetType 引用它；全字段必填 |
| `components` | 提示作者要配哪些块的元数据，不驱动校验（校验在 fx-04 模板侧）；组件名→配置块名映射见 reference，TargetFilterParams 无 preset 声明 |
| `activePhases` / `allowedLifetimes` | 该原型的活跃相位与允许寿命集合，模板 lifetime 须在其中 |
| `defaultPhaseHandlers` | 各相位默认处理器；`type` 仅 `builtin` 或 `graph` |

全表（components 空 = 无参数块，直接执行处理器）：

| id | 组件 | 活跃相位 | 允许寿命 | 默认处理器 |
|---|---|---|---|---|
| InstantDamage | modifiers | OnApply | Instant | builtin ApplyModifiers |
| Heal | modifiers | OnApply | Instant | builtin ApplyModifiers |
| Buff | modifiers+duration | OnApply | After/Infinite | builtin ApplyModifiers |
| DoT | modifiers+duration | OnApply+OnPeriod | After/Infinite | builtin ApplyModifiers（两相位同） |
| HoT | modifiers+duration | OnApply+OnPeriod | 仅 After | builtin ApplyModifiers（两相位同） |
| ApplyForce2D | configParams 力保留键 | OnApply | Instant | builtin ApplyForce |
| Search | targetQuery+targetDispatch | OnResolve+OnApply | Instant | SpatialQuery + DispatchPayload |
| PeriodicSearch | targetQuery+targetDispatch+duration | OnPeriod | 仅 After | builtin ReResolveAndDispatch |
| LaunchProjectile | projectile | OnApply | Instant | builtin CreateProjectile |
| CreateUnit | unitCreation | OnApply | Instant | builtin CreateUnit |
| Displacement | — | OnApply | Instant | builtin ApplyDisplacement |
| Relation | relation | OnApply | Instant | builtin ApplyRelation |
| Exchange | — | OnApply | Instant | builtin ExecuteExchange |
| CompleteProgression | — | OnApply | Instant | builtin CompleteProgression |
| SubmitOrderFromBlackboard | — | OnApply | Instant | builtin SubmitOrderFromBlackboard |
| DeployConsumeSource | — | OnApply | Instant | graph Graph.Lifecycle.DeployConsumeSource |

## 3. 文件结构

`assets/GAS/preset_types.json`，分片目录 目录条目 `GAS/preset_types/`（分片目录，根数据为空）；mod 自定义原型写入同路径。加载序在 graphs 之后、effects 之前。

## 4. 运行时加载效果

逐条加载注册（全字段必填）；内建枚举名占固定 id，mod 原型从独立 id 段起（数值见 reference），注册表 Freeze 后关闭。效果模板引用时先查本注册表再查内建枚举。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 原型任一字段缺失 / type 非 builtin·graph | 启动失败 |
| Freeze 后注册、超出 mod id 段上限 | 启动失败 |
| 效果引用未注册 presetType | 启动失败（fx-04） |

## 6. 实例

- `assets/GAS/preset_types.json`（16 条全量）；消费侧见 `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（Buff/DoT/Search/LaunchProjectile/CreateUnit 皆有实例）

**相关文档**：[fx-05 PRD](../prd/fx-03-preset-types.md) · [fx-04 配置说明](fx-02-template.md)
