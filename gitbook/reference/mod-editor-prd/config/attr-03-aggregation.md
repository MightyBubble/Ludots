# attr-03 配置说明 · 聚合管线

> 配置写法与行为。第一性需求见 [attr-03 PRD](../prd/attr-03-aggregation.md)；编辑器需求见 [UXD](../uxd/attr-03-aggregation.md)；现状见 [reference](../reference/attr-03-aggregation.md)。

## 1. 示例配置

聚合没有独立配置文件：资格随效果的 `presetType` 声明。演示底座真实 Buff（`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`，建造状态，节选）：

```json
{
  "id": "Effect.Rts.RedAlert.Construction",
  "presetType": "Buff",
  "lifetime": "After",
  "duration": { "durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame" }
}
```

Buff 携带修改器即聚合叠加（教学骨架——移速增益，过期自动消退）：

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
| `presetType: "Buff"` | 该效果的修改器进聚合重算；其余预设即时落点（见 attr-02） |
| `lifetime` / `duration` | 决定叠加持续的窗口；过期即退出重算 |
| `modifiers` | 聚合路径逐效果叠加，条数上限见 [事实与取值表](../facts.md) |

注意：聚合资格目前只能经 Buff 预设获得，配置面没有独立开关——新增预设默认不聚合（已知缺口，A4）。

## 3. 文件结构

无独立文件；聚合由效果表（`assets/GAS/effects.json` 及分片）与实体模板初值共同决定，见 fx-05 与 ent-01。

## 4. 运行时加载效果

效果实体创建时按 presetType 打聚合标志；此后效果应用、入栈、移除、图内取消、事务取消、装备授予都会给宿主打聚合脏，下一帧重算。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 重算实体缺脏组件 | 即错（系统级，不可配置） |
| 派生绑定缺图程序 | 运行期抛错（见 attr-04） |

## 6. 实例

- Buff 聚合资格：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（Construction 族）
- 修改器容量：[事实与取值表](../facts.md)

**相关文档**：[attr-03 PRD](../prd/attr-03-aggregation.md) · [attr-02 配置说明](attr-02-modifiers.md)
