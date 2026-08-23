#### 案 7：panel.relation.indicator —— 关系图指示（纯展示）

> 状态：🟢 今日可装载——纯展示；关系边=图内读取。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["关系 7 边", "强度 3.2"]}
```

```jsonc
{
  "id": "panel.relation.indicator",
  "graph": "Graph.Relation.Indicator",        // 图内读关系边（同盟/贸易/敌对）+ 强度
  "pins": [
    { "name": "edge",     "key": "relation.edge",     "mode": "realtime", "default": 0 },
    { "name": "strength", "key": "relation.strength", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Relation.Indicator（kind: Query）
{
  "id": "Graph.Relation.Indicator", "kind": "Query", "entry": "caster",
  "nodes": [
    { "id": "caster",   "op": "LoadCaster" },
    { "id": "edges",    "op": "RelationshipQueryOutgoing", "relationshipType": "Ally" },
    { "id": "edge",     "op": "AggCount" },
    { "id": "strength", "op": "RelationshipAggSumMetric", "relationshipType": "Ally", "metric": "Strength" }
  ],
  "controlEdges": [
    { "from": "caster", "fromPort": "next", "to": "edges" },
    { "from": "edges",  "fromPort": "next", "to": "edge" },
    { "from": "edge",   "fromPort": "next", "to": "strength" }
  ],
  "valueEdges": [
    { "from": "caster", "fromPort": "value", "to": "edges",    "toPort": "source" },
    { "from": "edges",  "fromPort": "list",  "to": "edge",     "toPort": "list" },
    { "from": "edges",  "fromPort": "list",  "to": "strength", "toPort": "list" }
  ],
  "outputs": [
    { "id": "edge",     "destination": "Summary", "type": "Int",   "source": "edge",     "key": "relation.edge" },
    { "id": "strength", "destination": "Summary", "type": "Float", "source": "strength", "key": "relation.strength" }
  ]
}
```

```text
世界锚点（关系边两端）┌──── 虚线 ────┐
   城市A ─················· 城市B     同盟蓝线/敌对红线，线宽随 strength（皮层渲染）
```

30 秒预期：结盟后两城间出蓝线，断交变红。依赖：无。
