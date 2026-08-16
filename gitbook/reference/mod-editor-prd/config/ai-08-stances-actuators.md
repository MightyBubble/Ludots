# ai-07 配置说明 · 战斗姿态与执行器门

> 配置写法与行为。第一性需求见 [ai-07 PRD](../prd/ai-08-stances-actuators.md)；编辑器需求见 [UXD](../uxd/ai-08-stances-actuators.md)；现状见 [reference](../reference/ai-08-stances-actuators.md)。

## 1. 示例配置

真实例（utility_autocast 两表现状全量——空占位）：

```json
[]
```

教学骨架（两表写法，标"编译保留"）：

```json
[
  { "id": "Stance.Example.HoldFire",
    "TargetFilter": "TF.Example.Precise",
    "AutoAcquire": false, "Retaliate": true, "AllowMoveChase": false }
]
```

```json
[
  { "id": "Actuator.Example.MainGun",
    "AbilityKey": "Ability.Example.Fire",
    "ReadinessInput": "In.Example.Ready01",
    "AimGateInput": "In.Example.SkillUp" }
]
```

## 2. 字段与行为

stances：

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| TargetFilter | 可选 | 姿态专属目标过滤器引用 |
| AutoAcquire / Retaliate / AllowMoveChase | false | 索敌/反击/追击许可位 |

actuators：

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| AbilityKey（或 AbilityId） | 可选 | 执行器绑定的技能 |
| ReadinessInput | 可选 | 就绪度采样源（引 inputs 表） |
| AimGateInput | 可选 | 瞄准门采样源（引 inputs 表） |

实况组件：实体模板可注入 ActuatorReadiness（ActuatorId/Ready01/BlockReason/EtaSteps/RequiresPreparation）与 AimGate（ActuatorId/Ready01/BlockReason）；门控统一走 PassesActuatorGates。

现状注意：stance 编译了但无系统消费（问题 I6：UtilityAiStanceState 无读写，仅 AIInspector 打印长度）；两个 showcase 的 stances/actuators 是永远为空的 [] 占位（问题 I7）。

## 3. 文件结构

目录条目 `AI/stances.json`（根数据为空，由 mod 贡献）、目录条目 `AI/actuators.json`（根数据为空，由 mod 贡献）（各自 ArrayById）。组件注入见 ent-01 的模板组件写法。

## 4. 运行时加载效果

两表编译进 Stances/Actuators 数组并登记作者目录（profile 的 DefaultStance 在此解析）；actuator 的两个 input 引用在 inputs 表编译后核验。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| TargetFilter/ReadinessInput/AimGateInput 引用未定义 | 启动失败，带路径 |
| DefaultStanceId 数字写法 | 启动失败：Use DefaultStance with a stance key |
| stance 空转 | 不报错——编译保留，无消费（I6） |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/stances.json`、`actuators.json`（真实：均为空 []）

**相关文档**：[ai-07 PRD](../prd/ai-08-stances-actuators.md) · [ai-04 配置说明](ai-05-dm-profiles.md) · [ent-01 配置说明](ent-01-templates.md)
