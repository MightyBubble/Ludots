# ent-01 配置说明 · 实体模板

> 配置写法与行为。第一性需求见 [ent-01 PRD](../prd/ent-01-templates.md)；编辑器需求见 [UXD](../uxd/ent-01-templates.md)；现状见 [reference](../reference/ent-01-templates.md)。

## 1. 示例配置

演示底座的真实模板（`Entities/templates.json` 节选）：

```json
[ {
  "id": "rts_ra_team_anchor",
  "components": {
    "Team": { "Id": 1 },
    "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
    "AttributeBuffer": { "base": { "Credits": 0, "Power": 0, "Ore": 0 },
                          "current": { "Credits": 0, "Power": 0, "Ore": 0 } }
  }
} ]
```

带出生效果的形态（教学骨架）：

```json
[ { "id": "MyMod.Barracks", "onSpawnEffect": "Effect.MyMod.BarracksIncome",
    "components": { "Name": { "Value": "兵营" }, "Team": { "Id": 1 } } } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 模板唯一名；地图布阵与造单位效果按名引用 |
| `onSpawnEffect` | 该模板实例化时自动施放的效果模板（经济建筑挂产出 buff 的通道） |
| `components` | 开放映射：组件名 → 初始值 JSON。引擎组件清单即合法键集；值形状由该组件自身决定（如 `AttributeBuffer` 的 base/current、`Team` 的 Id） |

地图实例的 `Overrides` 与之同构：按组件名给覆盖值，实例化时最后写入。

## 3. 文件结构

`Entities/templates.json`（目录登记的表，数组按 id 合并；引擎默认根当前为空，条目由各 mod 贡献），可分片、可被皮肤/强化 mod 按 id 深合并改数值。

## 4. 运行时加载效果

启动期随表加载注册（名字→模板）；地图加载时逐布阵条目实例化（模板组件 + 实例覆盖）；效果造单位（cfg 卷 5 的 CreateUnit）同走模板实例化，出生效果在实例化后施放。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 组件名不在引擎组件清单 | 启动失败，指明模板与组件名 |
| 组件初值不合组件解析 | 启动失败，指明字段 |
| 布阵/效果引用未注册模板 | 加载/执行失败，指明引用方 |

## 6. 实例

- 底座模板表：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Entities/templates.json`
- 出生效果消费方：效果卷 fx-20（造单位）

**相关文档**：[ent-01 PRD](../prd/ent-01-templates.md) · [map-01 配置说明](map-01-definition.md) · [cfg-04 配置说明](../config/cfg-04-config-tables.md)
