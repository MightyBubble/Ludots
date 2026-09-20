#### 案 6：panel.minimap —— 小地图（纯展示覆盖层）

> 状态：🟢 今日可装载——纯展示；图内聚合实体位置/阵营输出图层。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "🗺 己方 12 单位"}
```

```jsonc
{
  "id": "panel.minimap",
  "graph": "Graph.Map.Minimap",               // 图内聚合实体位置+阵营 → 输出图层数据
  "pins": [ { "name": "layer", "key": "minimap.layer", "mode": "realtime", "default": 0 } ]
  // 覆盖层画布：皮按 layer 渲染；缺数据=空图层（default 合同）
}
```

```jsonc
// 值图 Graph.Map.Minimap（kind: Query）
{
  "id": "Graph.Map.Minimap", "kind": "Query", "entry": "owner",
  "nodes": [
    { "id": "owner",  "op": "LoadCaster" },
    { "id": "allMap", "op": "QueryAllMapEntities" },
    { "id": "own",    "op": "QueryFilterTeam", "teamId": 2147483646 },
    { "id": "layer",  "op": "AggCount" }
  ],
  "controlEdges": [
    { "from": "owner",  "fromPort": "next", "to": "allMap" },
    { "from": "allMap", "fromPort": "next", "to": "own" },
    { "from": "own",    "fromPort": "next", "to": "layer" }
  ],
  "valueEdges": [
    { "from": "allMap", "fromPort": "list", "to": "own",   "toPort": "list" },
    { "from": "own",    "fromPort": "list", "to": "layer", "toPort": "list" }
  ],
  "outputs": [
    { "id": "layer", "destination": "Summary", "type": "Int", "source": "layer", "key": "minimap.layer" }
  ]
}
```

```text
screen.bottomLeft ┌──────────┐
                  │ ▦▦  ▦▦▦  │  己方蓝点/敌方红点（皮分层渲染）
                  └──────────┘
                  （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：兵移动蓝点同步动。依赖：无。
