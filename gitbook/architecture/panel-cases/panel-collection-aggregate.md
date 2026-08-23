#### 案 16：panel.collection.aggregate —— 集合聚合（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；聚合=图内节点（集合行为图本体随 #1012，缺数据落 default）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["24/30 部队", "均血 78%"]}
```

```jsonc
{
  "id": "panel.collection.aggregate",
  "graph": "Graph.Collection.Aggregate",      // 图内聚合集合计数/均值（#1012 集合行为主战场）
  "pins": [
    { "name": "count", "key": "collection.count", "mode": "realtime", "default": 0 },
    { "name": "avgHp", "key": "collection.avgHp", "mode": "realtime", "default": 0 },
    { "name": "cap",   "key": "collection.cap",   "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Collection.Aggregate（kind: Query）
{
  "id": "Graph.Collection.Aggregate", "kind": "Query", "entry": "squad",
  "nodes": [
    { "id": "squad", "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 400 },
    { "id": "count", "op": "AggCount" },
    { "id": "avgHp", "op": "AggAverageAttribute", "attribute": "Health" },
    { "id": "cap",   "op": "ConstInt", "intValue": 30 }
  ],
  "controlEdges": [
    { "from": "squad", "fromPort": "next", "to": "count" },
    { "from": "count", "fromPort": "next", "to": "avgHp" },
    { "from": "avgHp", "fromPort": "next", "to": "cap" }
  ],
  "valueEdges": [
    { "from": "squad", "fromPort": "list", "to": "count", "toPort": "list" },
    { "from": "squad", "fromPort": "list", "to": "avgHp", "toPort": "list" }
  ],
  "outputs": [
    { "id": "count", "destination": "Summary", "type": "Int",   "source": "count", "key": "collection.count" },
    { "id": "avgHp", "destination": "Summary", "type": "Float", "source": "avgHp", "key": "collection.avgHp" },
    { "id": "cap",   "destination": "Summary", "type": "Int",   "source": "cap",   "key": "collection.cap" }
  ]
}
```

```text
screen.topLeft（tab 下方）┌───────────────────┐
                          │ 部队 24/30 均血 78% │
                          └───────────────────┘
```

30 秒预期：部队增减数字同帧变化。依赖：无。
