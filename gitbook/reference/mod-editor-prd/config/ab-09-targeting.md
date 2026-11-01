# ab-09 配置说明 · Targeting 与组合命令

> 配置写法与行为。第一性需求见 [ab-09 PRD](../prd/ab-09-targeting.md)；编辑器需求见 [UXD](../uxd/ab-09-targeting.md)；现状见 [reference](../reference/ab-09-targeting.md)。

## 1. 示例配置

真实实例（champion 沙盒：Ezreal E 位移，射程 440cm）：

```json
{
  "targeting": { "castRangeCm": 440 }
}
```

自施技能（champion 沙盒：Garen.Judgment 旋转伤害，0 = 不吃距离与走近）：`"targeting": { "castRangeCm": 0 }`。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `targeting.castRangeCm` | 必填非负：施法距离上限（cm）；0 = 自施，不吃距离与走近 |

`targeting` 只收 `castRangeCm`。指示器要的长度、半径、角度不写在这里：形状图用 `LoadAbilityEffectFloat` 从这招 `exec` 放出的效果上读（见 [552 wiki](../../graph-node-op-wiki/LoadAbilityEffectFloat.md)）。

超射程行为（非配置项，由组合命令计划器决定）：显式目标超出 castRangeCm 时自动生成"移动到射程边缘 + 施放"计划；排队模式按移动完成后的预计位置判定。旧字段名 `targeting.range` 启动报错指路 `castRangeCm`。

## 3. 文件结构

targeting 是 `abilities.json` 单条技能的顶层块（ab-01）。自动选目标不在技能配置里，写在交互状态的施法图里（input-03）。组合命令不是配置——它是订单提交期的计划行为，作用于一切有射程声明的技能。

## 4. 运行时加载效果

castRangeCm 编为定档数值；运行期计划器在订单提交时读槽位技能的 targeting 决定裁剪。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| targeting 缺 castRangeCm / 负数 / 出现其它键 | 启动失败 |
| 旧字段 `targeting.range` / `targeting.impactEffect` / 顶层 `indicator` | 启动失败指路新写法 |
| 顶层 `effectFields` | 启动失败，按未知字段报错 |
| 续单队列满 | 组合计划拒绝（RejectedQueueFull） |
| 批量命令部分不可行 | 整批抛错（不部分执行） |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/abilities.json`（25 条 targeting，含 0 射程自施）
- `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json`（建造/部署类自施）

**相关文档**：[ab-09 PRD](../prd/ab-09-targeting.md) · [ab-01 配置说明](ab-01-definition.md) · [ord-03 配置说明](ord-03-pipeline.md)
