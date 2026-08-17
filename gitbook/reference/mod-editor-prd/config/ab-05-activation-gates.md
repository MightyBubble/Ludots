# ab-05 配置说明 · 激活门

> 配置写法与行为。第一性需求见 [ab-05 PRD](../prd/ab-05-activation-gates.md)；编辑器需求见 [UXD](../uxd/ab-05-activation-gates.md)；现状见 [reference](../reference/ab-05-activation-gates.md)。

## 1. 示例配置

演示场景真实用例（rts 底座：建造中禁再下单）：

```json
"blockTags": { "blockedAny": [ "State.Rts.RedAlert.Constructing" ] }
```

全门骨架（教学骨架）：

```json
{
  "id": "Ability.Ex.Gated",
  "blockTags": { "requiredAll": [ "State.Ex.Stance" ], "blockedAny": [ "Status.Stunned" ] },
  "activationPrecondition": { "validationGraph": "Graph.Ex.CanCastNuke" },
  "useRequirement": "Progression.Ex.NukeUnlocked",
  "showRequirement": "Progression.Ex.NukeVisible",
  "exec": { "clockId": "FixedFrame", "items": [ { "kind": "End", "tick": 0 } ] }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `blockTags.requiredAll` | 激活时施法者有效 tag 须全部在场 |
| `blockTags.blockedAny` | 任一在场即拒激活（冷却闭环用它挡，见 ab-04） |
| `activationPrecondition.validationGraph` | 起播前跑该 Validation 图，图假即拒（可读施法者/目标/目标坐标） |
| `useRequirement` | 进度需求 id：不满足则不可用（挡激活） |
| `showRequirement` | 进度需求 id：不满足则不可见（不挡激活） |

目标校验与槽位不是配置字段：目标校验读订单参数（目标上下文存活、显式目标存活、目标集合非空且全存活），槽位读槽位系统（ab-06）。

判序（订单起播入口）：toggle 关闭检查 → tag 门 → 进度需求（use）→ 前置图；进度需求要求显式范围且首个条目是输入/目标收集门时延迟到门响应后判。

## 3. 文件结构

三个门块都是 `abilities.json` 单条技能的顶层字段（ab-01）；validationGraph 引用 `GAS/graphs.json` 已注册图，进度需求引用进度域注册名。

## 4. 运行时加载效果

blockTags 编译为激活门掩码；前置图解析为图程序引用（须先注册）；进度需求解析为注册 id。运行期每次激活逐关评估，全部只读不写。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| tag 门不通过 / 前置图为假 / 进度不满足 | 本次激活拒绝（各有独立失败原因） |
| validationGraph 缺失或未注册 | 启动失败 |
| useRequirement/showRequirement 未知名 | 启动失败 |
| 槽位无效 / 黑板槽键缺失 | 订单失败（InvalidSlot / MissingBlackboardSlot） |

## 6. 实例

- blockedAny 实例：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json`（全部建造/训练技能）
- requiredAll+blockedAny 实例：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/ability_form_sets.json`（route 同语义对照，ab-07）

**相关文档**：[ab-05 PRD](../prd/ab-05-activation-gates.md) · [ab-01 配置说明](ab-01-definition.md) · [tag-02 配置说明](tag-02-rules.md)
