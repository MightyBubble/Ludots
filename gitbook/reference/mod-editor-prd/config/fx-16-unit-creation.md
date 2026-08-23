# fx-16 配置说明 · 造单位

> 配置写法与行为。第一性需求见 [fx-16 PRD](../prd/fx-16-unit-creation.md)；编辑器需求见 [UXD](../uxd/fx-16-unit-creation.md)；现状见 [reference](../reference/fx-16-unit-creation.md)。

## 1. 示例配置

真实条目一（`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`，放建筑 + 出生效果链）：

```json
{
  "id": "Effect.Rts.RedAlert.PlacePowerPlant",
  "presetType": "CreateUnit",
  "lifetime": "Instant",
  "unitCreation": {
    "templateId": "rts_ra_power_plant",
    "placementPattern": "Scatter",
    "count": 1,
    "onSpawnEffect": "Effect.Rts.RedAlert.Construction",
    "copySourcePlayerOwner": true
  }
}
```

真实条目二（`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`，环形召唤，Skeleton）：`unitType: "Unit.Skeleton"`、`count: 1`、`placementPattern: "Circle"`、`facingPattern: "RadialOutward"`、`placementRadiusCm: 200`、`placementStartAngleDeg: 0`、`copySourcePlayerOwner: true`。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `unitType` / `templateId` | 二选一：unitType 走轻量装配；templateId 走实体模板完整实例化（见 ent-01） |
| `count` | 生成数量；非正数启动失败 |
| `placementPattern` | `Scatter`：以出生点为心的散布；`Circle`：指定半径的环形 |
| `offsetRadius` | 仅 Scatter：散布半径，非负 |
| `placementRadiusCm` / `placementStartAngleDeg` | 仅 Circle：环半径（必填正数）与起始角（必填） |
| `facingPattern` | 朝向：PreserveTemplate（缺省）/RadialOutward/TangentClockwise/TangentCounterClockwise；Scatter 下禁写 |
| `onSpawnEffect` | 可选：单位落地时施加的效果模板 |
| `copySourcePlayerOwner` | 只可 true 或省略：继承源的玩家归属 |
| `linkSourceAsParent` | 只可 true 或省略：把源挂为父实体 |

两式的互斥字段（Scatter 禁 facingPattern/placementRadiusCm/placementStartAngleDeg；Circle 禁 offsetRadius）写了即启动失败。块只允许挂在 `presetType: CreateUnit` + Instant。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `unitCreation` 块；templateId 指向 `Entities/templates.json` 的模板（见 ent-01），onSpawnEffect 指向同表效果条目。

## 4. 运行时加载效果

loader 校验组合与互斥字段，unitType 首现注册、子效果名解析为模板 id；运行期按 count 循环计算摆放与朝向后入生成队列。数值改动经工作台热通道为下次施放生效级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| unitType 与 templateId 同有/同无 | 启动失败，报"exactly one" |
| 图案互斥字段违例；Circle 半径 <=0 或缺起始角；count 非正 | 启动失败，指明字段与图案 |
| onSpawnEffect 未注册 | 启动失败，指明名字 |
| 生成队列容量满 | 运行期抛错 |

## 6. 实例

- 放建筑与出兵：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（PlacePowerPlant、TrainRhino 全族）
- 环形召唤：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`（Summon.Skeleton）

**相关文档**：[fx-16 PRD](../prd/fx-16-unit-creation.md) · [ent-01 配置说明](ent-01-templates.md)
