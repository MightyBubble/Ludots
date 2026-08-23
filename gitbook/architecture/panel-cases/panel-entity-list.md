#### 案 13：panel.entity.list —— 实体列表（交互选中）

> 状态：🔴（配置可装载）——运行链路：G8/$payload、#1015、条目列表依赖 G12（列表型引脚）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "▸ 3 单位"}
```

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",               // 图内过滤+排序集合 → 行数据
  "pins": [ { "name": "rowCount", "key": "list.rowCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "entity.pick", "control": "list.entities", "gesture": "click", "payload": { "row": "Int" } } ],
  "intents": [ { "event": "entity.pick", "intent": "selection.setTarget", "args": { "row": "$payload.row" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```jsonc
// 值图 Graph.Entity.List（kind: Query）
{
  "id": "Graph.Entity.List", "kind": "Query", "entry": "caster",
  "nodes": [
    { "id": "caster",  "op": "LoadCaster" },
    { "id": "all",     "op": "QueryAllMapEntities" },
    { "id": "team",    "op": "QueryFilterTeam", "teamId": 2147483646 },
    { "id": "sorted",  "op": "QuerySortByAttribute", "attribute": "Health", "descending": true },
    { "id": "rowCount", "op": "AggCount" }
  ],
  "controlEdges": [
    { "from": "caster",  "fromPort": "next", "to": "all" },
    { "from": "all",     "fromPort": "next", "to": "team" },
    { "from": "team",    "fromPort": "next", "to": "sorted" },
    { "from": "sorted",  "fromPort": "next", "to": "rowCount" }
  ],
  "valueEdges": [
    { "from": "all",     "fromPort": "list", "to": "team",     "toPort": "list" },
    { "from": "team",    "fromPort": "list", "to": "sorted",   "toPort": "list" },
    { "from": "sorted",  "fromPort": "list", "to": "rowCount", "toPort": "list" }
  ],
  "outputs": [
    { "id": "rowCount", "destination": "Summary", "type": "Int", "source": "rowCount", "key": "list.rowCount" }
  ]
}
```

```text
screen.leftCenter ┌──────────────────────┐
                  │ ▸ 长枪兵    HP 82%   │ 点击行→选中对应实体（行→实体映射在图内）
                  │ ▸ 弓手      HP 64%   │
                  │ ▸ 医师      HP 97%   │
                  └──────────────────────┘
                  （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：点列表行对应单位被选中并高亮。依赖：G8、#1015。
