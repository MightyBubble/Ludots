#### 案 12：panel.road.indicator —— 路网指示（纯展示）

> 状态：🟢 今日可装载——纯展示；路网状态=图内读取。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "⇗ 商路 3 条 · 畅通"}
```

```jsonc
{
  "id": "panel.road.indicator",
  "graph": "Graph.Map.Road",                  // 图内读路网状态（通畅/拥堵/损毁）
  "pins": [
    { "name": "roadState", "key": "road.state", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Map.Road（kind: Query）
{
  "id": "Graph.Map.Road", "kind": "Query", "entry": "roadState",
  "nodes": [
    { "id": "roadState", "op": "LoadSelfAttribute", "attribute": "Map.Road.State" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "roadState", "destination": "Summary", "type": "Int", "source": "roadState", "key": "road.state" }
  ]
}
```

```text
世界锚点（路径上）┌──────────────────────────┐
                  │ ═══ 通畅 ═══   拥堵=黄闪   │ 行军路线高亮，状态决定颜色
                  └──────────────────────────┘
```

30 秒预期：点选行军路线路网高亮，拥堵变黄。依赖：无。

### 分组三 · 选择与实体（案 13–18）
