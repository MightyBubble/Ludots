#### 案 8：panel.selection.marker —— 选中标记（纯展示）

> 状态：🟢 今日可装载——纯展示；选中集合=图内读取。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["◎ 长枪兵", "×12 已选中"]}
```

```jsonc
{
  "id": "panel.selection.marker",
  "graph": "Graph.Selection.Marker",          // 图内读 selection 集合 → 计数/形态
  "pins": [
    { "name": "selectedCount", "key": "selection.count", "mode": "realtime", "default": 0 },
    { "name": "selectionKind", "key": "selection.kind", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Selection.Marker（kind: Query）
{
  "id": "Graph.Selection.Marker", "kind": "Query", "entry": "selection",
  "nodes": [
    { "id": "selection",     "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 200 },
    { "id": "selectedCount", "op": "AggCount" },
    { "id": "selectionKind", "op": "ConstInt", "intValue": 0 }
  ],
  "controlEdges": [
    { "from": "selection",     "fromPort": "next", "to": "selectedCount" },
    { "from": "selectedCount", "fromPort": "next", "to": "selectionKind" }
  ],
  "valueEdges": [
    { "from": "selection", "fromPort": "list", "to": "selectedCount", "toPort": "list" }
  ],
  "outputs": [
    { "id": "selectedCount", "destination": "Summary", "type": "Int", "source": "selectedCount", "key": "selection.count" },
    { "id": "selectionKind", "destination": "Summary", "type": "Int", "source": "selectionKind", "key": "selection.kind" }
  ]
}
```

```text
世界锚点（选中单位脚下）┌───────────┐
                        │ ◉ 光圈      │ 单选=光圈；框选=三角阵（皮按 kind 画）
                        └───────────┘
```

30 秒预期：点选出光圈、框选变多选标记。依赖：无。
