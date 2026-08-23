#### 案 19：panel.events.feed —— 事件面板（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；滚动/展开=皮层手势不声明意图。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "list", "rows": [["12:01", "北门遇袭"], ["12:03", "援军抵达"]]}
```

```jsonc
{
  "id": "panel.events.feed",
  "graph": "Graph.Events.Feed",               // 图内取最近 N 条事件（新→旧）
  "pins": [ { "name": "feedCount", "key": "events.feedCount", "mode": "realtime", "default": 0 } ]
}
```

```text
screen.topRight 下方 ┌─────────────────────────┐
                     │ ⚔ 遭遇战开始 08:12      │ 新事件顶入，超 N 条溢出
                     │ ⚒ 铁矿 +50     08:11   │
                     └─────────────────────────┘
```

30 秒预期：战斗打响事件顶入首行。依赖：无。
