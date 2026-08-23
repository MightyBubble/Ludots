#### 案 3：panel.tabs.global —— 全局功能 tab（交互路由）

> 状态：🔴 目标态——拒因：G8（$payload 引用语义）+ G9（actorSource none）+ #1015（意图链路本体）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["信息", "科技", "外交", "生产"], "on": 0}
```

```jsonc
{
  "id": "panel.tabs.global",
  "graph": "Graph.UI.Tabs",                   // 回读 ui.activeTab 高亮
  "pins": [ { "name": "activeTab", "key": "ui.activeTab", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "tab.switch", "control": "tab.bar", "gesture": "click", "payload": { "tab": "Int" } } ],
  "intents": [ { "event": "tab.switch", "intent": "ui.switchTab", "args": { "tab": "$payload.tab" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.topLeft（信息聚合下方）┌────────────────────────────┐
                             │【信息】【科技】【外交】【生产】│ ← activeTab 高亮
                             └────────────────────────────┘
```

30 秒预期：点科技→右侧切科技面板，再点外交切走。依赖：G3、G8、G9、#1015、子系统面板本体（2.9）。
