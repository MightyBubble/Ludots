#### 案 13：panel.entity.list —— 实体列表（展示名册）

> 状态：🟢（G12）——**图管圈人/排序**，面板 `lists` 只绑列 + `layout` 声明控件。点击行→选中仍属 #1015。

> 配置合同 SSOT：[面板视图投影](../panel-view-projection.md)。Showcase：`mods/showcases/panel_entity_list/PanelEntityListShowcaseMod`。

> **高保真预期**：

```mock
{"type": "text", "text": "在编 4 · 指挥官 HP100 · 医师 HP97 · 晕眩卫士[晕眩] · 弓手 HP64"}
```

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",
  "pins": [
    { "name": "rowCount", "key": "panel.roster.rowCount", "mode": "realtime", "default": 0 }
  ],
  "lists": [
    {
      "name": "units",
      "collectionKey": "panel.roster.units",
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
        "type": "list",
        "bind": "units",
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
// Graph.Entity.List —— 过滤/排序只在这里
{
  "id": "Graph.Entity.List", "kind": "Query", "entry": "all",
  "nodes": [
    { "id": "all", "op": "QueryAllMapEntities" },
    { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
    { "id": "minHp", "op": "ConstFloat", "floatValue": 0.001 },
    { "id": "maxHp", "op": "ConstFloat", "floatValue": 999999 },
    { "id": "alive", "op": "QueryFilterAttributeRange", "attribute": "Health" },
    { "id": "sorted", "op": "QuerySortByAttribute", "attribute": "Health", "descending": true },
    { "id": "rowCount", "op": "AggCount" }
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
               │ 在编 4               │
               │ 指挥官   ████ 100    │
               │ 医师     ███░  97    │
               │ 晕眩卫士 ██░░  80 [晕眩]
               │ 弓手     ██░░  64    │
               └──────────────────────┘
```

30 秒预期：名单与顺序跟图走；面板模板零查询语义。
