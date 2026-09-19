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

增量继承的形态（英雄 = 小兵 + 增量）：

```json
[ { "id": "MyMod.Grunt", "components": { "Name": { "Value": "步兵" }, "Team": { "Id": 1 },
    "AttributeBuffer": { "base": { "Health": 100 } } } },
  { "id": "MyMod.Hero", "extends": "MyMod.Grunt",
    "components": { "AttributeBuffer": { "base": { "Health": 250, "Mana": 40 } } },
    "TriggerGraphs": [ "MyMod.HeroAura" ] } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 模板唯一名；地图布阵与造单位效果按名引用 |
| `extends` | 装载期继承：引用另一模板 id（可跨 mod，展开发生在同 id 合并之后）。components 按字段级深合并——子代字段胜，未提及字段继承父代，数组整体替换；children / TriggerGraphs 追加（TriggerGraphs 精确去重，同图双挂不是合法组合）；onSpawnEffect / initialInteractionContext 子代非空才覆盖。子代组件顶层 `"__replace": true` 时整组件替换父代值（变体形状组件通道）。物化只消费展开后的合并结果 |
| `onSpawnEffect` | 该模板实例化时自动施放的效果模板（经济建筑挂产出 buff 的通道） |
| `components` | 开放映射：组件名 → 初始值 JSON。引擎组件清单即合法键集；值形状由该组件自身决定（如 `AttributeBuffer` 的 base/current、`Team` 的 Id） |

地图实例的 `Overrides` 与之同构：按组件名给覆盖值，实例化时与模板组件做**字段级深合并**——覆盖只写改动的字段，未提及字段继承模板值；模板没有的组件按覆盖值整个添加。变体形状组件（如 `RegionVolumeCm` 圆形换矩形）不能字段合并，在覆盖对象顶层写 `"__replace": true` 回到整组件替换（标记沿 ConfigMerger `__delete` 的保留字惯例，装配前剥离）；`extends` 子代组件同样支持该标记。

## 3. 文件结构

`Entities/templates.json`（目录登记的表，数组按 id 合并；引擎默认根当前为空，条目由各 mod 贡献），可分片、可被皮肤/强化 mod 按 id 深合并改数值。`extends` 在同 id 合并之后展开，所以子模板与父模板可以来自不同 mod 的不同片段。

## 4. 运行时加载效果

启动期随表加载注册（名字→模板），装载顺序：同 id 合并 → extends 展开 → 模板校验（children 引用图、TriggerGraphs、出生效果引用）；地图加载时逐布阵条目实例化（模板组件 + 实例覆盖深合并）；效果造单位（cfg 卷 5 的 CreateUnit）同走模板实例化，出生效果在实例化后施放。离线烘焙侧（导航障碍目录）接入同一展开器，与运行时看到同一份模板。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 组件名不在引擎组件清单 | 启动失败，指明模板与组件名 |
| 组件初值不合组件解析 | 启动失败，指明字段 |
| 布阵/效果引用未注册模板 | 加载/执行失败，指明引用方 |
| `extends` 引用不存在的模板 / 继承环（含自继承） | 启动失败，指明子模板与父模板名 |

## 6. 实例

- 底座模板表：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/Entities/templates.json`
- 出生效果消费方：效果卷 fx-16（造单位）

**相关文档**：[ent-01 PRD](../prd/ent-01-templates.md) · [map-01 配置说明](map-01-definition.md) · [cfg-04 配置说明](../config/cfg-04-config-tables.md)
