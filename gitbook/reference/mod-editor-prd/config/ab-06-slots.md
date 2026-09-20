# ab-06 配置说明 · 槽位系统

> 配置写法与行为。第一性需求见 [ab-06 PRD](../prd/ab-06-slots.md)；编辑器需求见 [UXD](../uxd/ab-06-slots.md)；现状见 [reference](../reference/ab-06-slots.md)。

## 1. 示例配置

底座槽——实体模板 `AbilityStateBuffer.abilityIds`（数组下标即槽号；演示场景真实实例）：

```json
{ "AbilityStateBuffer": { "abilityIds": [
    "Ability.Rts.RedAlert.BuildPowerPlant",
    "Ability.Rts.RedAlert.BuildRefinery",
    "Ability.Rts.RedAlert.BuildWarFactory",
    "Ability.Rts.RedAlert.Hold" ] } }
```

物品授予槽——物品定义 `abilityGrants`（item_system 沙盒真实实例；装备后占格）：

```json
"abilityGrants": [ { "slotIndex": 4, "ability": "Ability.ItemShowcase.SecondWind" } ]
```

形态槽见 ab-07（`ability_form_sets.json` 的 slotOverrides）。临时授予层无配置面（运行时组件）。

## 2. 字段与行为

| 写法 | 产生什么效果 |
|---|---|
| 模板 `AbilityStateBuffer.abilityIds[]` | 出生落底座槽：第 n 个元素占槽 n，≤8，超出启动失败 |
| 物品 `abilityGrants[].slotIndex` + `ability` | 装备该物品后覆盖指定槽；卸下即让出 |
| 形态 route `slotOverrides[].slotIndex` | 形态匹配期间覆盖指定槽，每帧随状态进出（ab-07） |
| （无配置面）临时授予层 | 运行时按来源 tag 授予/回收 |

解析序：临时授予 > 物品 > 形态 > 底座；上层有覆盖则短路。`showRequirement` 不满足的技能在面板隐藏但槽仍解析。

## 3. 文件结构

底座槽在 `Entities/templates.json` 组件初值（ent-01）；物品授予在 `Items/definitions.json`（misc-02）；形态在 `GAS/ability_form_sets.json`（ab-07）。槽位无独立配置文件。

## 4. 运行时加载效果

模板编译时 abilityIds 解析为注册 id（未注册名启动失败）；物品定义编译时 abilityGrants 同样解析；运行期装备同步系统把已装备物品的授予写入物品槽层。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| abilityIds 引用未注册技能 | 启动失败 |
| 超过 8 个底座技能 | 启动失败（模板层上限校验） |
| slotIndex 越界（物品/形态） | 该授予被忽略（形态侧另有加载期拒绝，见 ab-07） |
| 槽号越界的解析请求 | 解析失败（无技能） |

## 6. 实例

- 底座槽：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Entities/templates.json`
- 物品授予：`mods/showcases/item_system/ItemSystemShowcaseMod/assets/Items/definitions.json`
- 形态槽：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/ability_form_sets.json`

**相关文档**：[ab-06 PRD](../prd/ab-06-slots.md) · [ent-01 配置说明](ent-01-templates.md) · [ab-07 配置说明](ab-07-form-sets.md)
