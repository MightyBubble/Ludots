#### 案 19：panel.events.feed —— 事件面板（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；滚动/展开=皮层手势不声明意图。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "⚔ 事件 2 条"}
```

```jsonc
{
  "id": "panel.events.feed",
  "graph": "Graph.Events.Feed",               // 图内取最近 N 条事件（新→旧）
  "pins": [ { "name": "feedCount", "key": "events.feedCount", "mode": "realtime", "default": 0 } ]
}
```

```jsonc
// 值图 Graph.Events.Feed（kind: Query）
{
  "id": "Graph.Events.Feed", "kind": "Query", "entry": "feedCount",
  "nodes": [
    { "id": "feedCount", "op": "LoadSelfAttribute", "attribute": "Events.FeedCount" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "feedCount", "destination": "Summary", "type": "Int", "source": "feedCount", "key": "events.feedCount" }
  ]
}
```

```text
screen.topRight 下方 ┌─────────────────────────┐
                     │ ⚔ 北门遇袭     12:01    │ 新事件顶入，超 N 条溢出
                     │ ⚒ 援军抵达     12:03    │
                     └─────────────────────────┘
                     （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：战斗打响事件顶入首行。依赖：无。
