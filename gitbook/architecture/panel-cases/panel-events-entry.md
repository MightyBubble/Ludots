#### 案 20：panel.events.entry —— 日志入口（交互）

> 状态：🔴（配置可装载）——运行链路缺口：#1015（意图链路）；args 为空不涉 G8/G9。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["📜 日志 (7)"], "on": 0}
```

```jsonc
{
  "id": "panel.events.entry",
  "graph": "Graph.Events.Entry",              // 未读日志数输出
  "pins": [ { "name": "unread", "key": "events.unread", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "log.open", "control": "btn.log", "gesture": "click" } ],
  "intents": [ { "event": "log.open", "intent": "ui.openLog", "args": {}, "playerSource": "seat", "actorSource": "none" } ]
}
```

```jsonc
// 值图 Graph.Events.Entry（kind: Query）
{
  "id": "Graph.Events.Entry", "kind": "Query", "entry": "unread",
  "nodes": [
    { "id": "unread", "op": "LoadSelfAttribute", "attribute": "Events.Unread" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "unread", "destination": "Summary", "type": "Int", "source": "unread", "key": "events.unread" }
  ]
}
```

```text
screen.bottomRight（子系统入口上方）┌──────┐
                                   │【📜7】│ ← unread 角标；点击开案21 日志
                                   └──────┘
```

30 秒预期：战斗后角标+1，点开日志清零。依赖：G8、G9、#1015、日志面板本体（案21）。
