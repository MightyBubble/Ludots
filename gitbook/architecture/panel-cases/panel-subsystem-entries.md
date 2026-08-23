#### 案 5：panel.subsystem.entries —— 子系统入口（交互路由）

> 状态：🔴 目标态——拒因：G8（$payload）+ G9（actorSource none）+ #1015。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "🔬 科技 3 未读"}
```

```jsonc
{
  "id": "panel.subsystem.entries",
  "graph": "Graph.UI.Subsystems",             // tech.unread 等未读角标输出
  "pins": [ { "name": "techUnread", "key": "tech.unread", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "sub.open", "control": "bar.subsystems", "gesture": "click", "payload": { "sub": "Int" } } ],
  "intents": [ { "event": "sub.open", "intent": "ui.openSubsystem", "args": { "sub": "$payload.sub" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```jsonc
// 值图 Graph.UI.Subsystems（kind: Query）
{
  "id": "Graph.UI.Subsystems", "kind": "Query", "entry": "techUnread",
  "nodes": [
    { "id": "techUnread", "op": "LoadSelfAttribute", "attribute": "UI.Tech.Unread" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "techUnread", "destination": "Summary", "type": "Int", "source": "techUnread", "key": "tech.unread" }
  ]
}
```

```text
screen.bottomRight 竖条 ┌────┐
                        │【🔬3】│ ← 角标=techUnread（超 9 显示 9+）
                        │【🛠】│
                        └────┘
```

30 秒预期：科技完成角标+1，点开面板清零。依赖：G3、G8、G9、#1015、路由机制（#29）。

### 分组二 · 地图/空间指示（案 6–12）
