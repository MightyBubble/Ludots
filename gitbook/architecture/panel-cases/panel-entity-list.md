#### 案 13：panel.entity.list —— 实体列表（展示名册）

> 状态：🟢（G12）——**图管圈人/排序**；**元素模板**声明 `subject` + 自有 graph；容器只透传成员并编排。点击行→选中仍属 #1015。

> 配置合同 SSOT：[面板视图投影](../panel-view-projection.md)。Showcase：`mods/showcases/panel_entity_list/PanelEntityListShowcaseMod`。

> **高保真预期**：

```mock
{"type": "text", "text": "在编 4 · 指挥官 HP100 · 医师 HP97 · 晕眩卫士[晕眩] · 弓手 HP64"}
```

**元素** — `panel.unit.roster`（`subject: Entity`，自带图）

```jsonc
{
  "id": "panel.unit.roster",
  "subject": "Entity",
  "graph": "Graph.Unit.RosterCard",
  "pins": [
    { "name": "health", "key": "unit.roster.health", "mode": "realtime", "default": 0 },
    { "name": "healthMax", "key": "unit.roster.healthMax", "mode": "realtime", "default": 0 },
    { "name": "stunned", "key": "unit.roster.stunned", "mode": "realtime", "default": 0 }
  ],
  "layout": {
    "controls": [
      { "type": "label", "bind": "displayName" },
      { "type": "progressBar", "current": "health", "max": "healthMax" },
      { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
    ]
  }
}
```

**List 容器** — 只编排，透传实体

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",
  "pins": [
    { "name": "rowCount", "key": "panel.roster.rowCount", "mode": "realtime", "default": 0 }
  ],
  "collections": [
    {
      "name": "units",
      "collectionKey": "panel.roster.units",
      "template": "panel.unit.roster"
    }
  ],
  "layout": {
    "controls": [
      { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
      {
        "type": "list",
        "bind": "units",
        "viewportHeight": 120,
        "itemExtent": 56,
        "virtualize": true,
        "overscan": 2
      }
    ]
  }
}
```

30 秒预期：名单跟容器图走；每一行以该实体为 scope 跑元素图；`viewportHeight` 可滚；`virtualize` 只画视口附近行；换 grid 可复用同一元素。千人压测见 `PanelListVirtualizationPerfTests`（窗口行数/分配量远低于全量）。
