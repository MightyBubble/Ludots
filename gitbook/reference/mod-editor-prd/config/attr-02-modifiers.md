# attr-02 配置说明 · 修改器

> 配置写法与行为。第一性需求见 [attr-02 PRD](../prd/attr-02-modifiers.md)；编辑器需求见 [UXD](../uxd/attr-02-modifiers.md)；现状见 [reference](../reference/attr-02-modifiers.md)。

## 1. 示例配置

演示底座真实造价效果（`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`，Instant 即时扣款，节选）：

```json
{
  "id": "Effect.Rts.RedAlert.CostPowerPlantStep",
  "presetType": "InstantDamage",
  "lifetime": "Instant",
  "participatesInResponse": false,
  "modifiers": [
    { "attribute": "Credits", "op": "Add", "value": -62.5 }
  ]
}
```

Buff 聚合修改器（教学骨架——乘算移速，随 Buff 过期自动消退）：

```json
{
  "id": "Effect.Example.Haste", "presetType": "Buff", "lifetime": "After",
  "duration": { "durationTicks": 90, "periodTicks": 0, "clockId": "FixedFrame" },
  "modifiers": [ { "attribute": "MoveSpeed", "op": "Multiply", "value": 1.5 } ]
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `attribute` | 落点属性名，首现注册（命名空间与上限见 attr-01） |
| `op` | `Add`=当前值+value；`Multiply`=当前值×value；`Override`=直接取 value |
| `value` | 有限数值，负数即扣减 |

同一属性多条按数组顺序依次执行，后者以前者为基。落点由 `presetType` 决定：非 Buff 预设即时执行；Buff 预设进聚合重算（见 attr-03 配置说明）。条数上限见 [事实与取值表](../facts.md)（EFFECT_MODIFIERS_CAPACITY）。

## 3. 文件结构

修改器是效果表条目的内嵌块，无独立文件；随 `assets/GAS/effects.json`（及分片 `GAS/effects/`）一起声明。

## 4. 运行时加载效果

效果模板加载时逐条写入定长修改器数组，属性名经注册表解析。运行期：即时修改器在效果落点执行（事务活跃先暂存、提交统一回写），聚合修改器等聚合重算叠加。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未注册属性 | 启动失败，指明效果与属性名 |
| 条数超上限（事实页） | 合同为加载失败指明条目；现状存在静默丢条缺口（A1） |

## 6. 实例

- 即时扣款与容量上限：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（Credits 造价族）；[事实与取值表](../facts.md)

**相关文档**：[attr-02 PRD](../prd/attr-02-modifiers.md) · [attr-03 配置说明](attr-03-aggregation.md)
