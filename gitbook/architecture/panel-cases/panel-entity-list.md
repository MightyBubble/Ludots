#### 案 13：panel.entity.list —— 实体列表（展示名册）

> 状态：🟢（G12 已落地）——声明式 `lists`/`layout` 驱动过滤、排序、血条与状态徽标；点击行→选中仍属 #1015，本 Showcase 纯展示。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "在编 5 · 指挥官 HP100 · 医师 HP97 · 晕眩卫士[晕眩] · 弓手 HP64"}
```

配置合同 SSOT：[面板视图投影](../panel-view-projection.md)。Showcase：`mods/showcases/panel_entity_list/PanelEntityListShowcaseMod`。

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",
  "pins": [ { "name": "rowCount", "key": "panel.roster.rowCount", "mode": "realtime", "default": 0 } ],
  "lists": [
    {
      "name": "units",
      "collectionKey": "panel.roster.units",
      "filter": [ { "kind": "attribute", "attribute": "Health", "op": "gt", "value": 0 } ],
      "sort": [ { "attribute": "Health", "descending": true } ],
      "item": {
        "fields": [
          { "name": "displayName", "kind": "name" },
          { "name": "health", "kind": "attribute", "attribute": "Health" },
          { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
          { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
        ]
      }
    }
  ],
  "layout": {
    "controls": [
      { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
      {
        "type": "list", "bind": "units",
        "itemControls": [
          { "type": "label", "bind": "displayName" },
          { "type": "progressBar", "current": "health", "max": "healthMax" },
          { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
        ]
      }
    ]
  }
}
```

```jsonc
// 值图 Graph.Entity.List（kind: Query）——图产出队伍全员集合；模板再滤存活并按血量排序
{
  "id": "Graph.Entity.List", "kind": "Query", "entry": "all",
  "nodes": [
    { "id": "all", "op": "QueryAllMapEntities" },
    { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
    { "id": "rowCount", "op": "AggCount" }
  ],
  "controlEdges": [
    { "from": "all", "fromPort": "next", "to": "team" },
    { "from": "team", "fromPort": "next", "to": "rowCount" }
  ],
  "valueEdges": [
    { "from": "all", "fromPort": "list", "to": "team", "toPort": "list" },
    { "from": "team", "fromPort": "list", "to": "rowCount", "toPort": "list" }
  ],
  "outputs": [
    {
      "id": "units",
      "destination": "EntityCollection",
      "type": "TargetList",
      "collectionKey": "panel.roster.units",
      "role": "Display"
    },
    { "id": "rowCount", "destination": "Summary", "type": "Int", "source": "rowCount", "key": "panel.roster.rowCount" }
  ]
}
```

```text
screen.topLeft ┌──────────────────────┐
               │ 在编 5               │
               │ 指挥官   ████ 100    │
               │ 医师     ███░  97    │
               │ 晕眩卫士 ██░░  80 [晕眩]
               │ 弓手     ██░░  64    │
               └──────────────────────┘
```

30 秒预期：进图即见左侧名册；只列存活单位且按血量从高到低；晕眩单位行上有徽标。点击选中留待 #1015。
