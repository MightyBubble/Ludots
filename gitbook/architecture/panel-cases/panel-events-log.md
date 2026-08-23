#### 案 21：panel.events.log —— 事件日志（交互模态）

> 状态：🔴 目标态——拒因：G5（modal.center）+ G8（$payload）+ G9 + G10（close 编排）+ #1015。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "list", "rows": [["春 7", "丰收 +200"], ["春 6", "暴风 −50"]]}
```

```jsonc
{
  "id": "panel.events.log",
  "graph": "Graph.Events.Log",                // 全量事件分页输出；过滤=图内节点
  "pins": [ { "name": "pageCount", "key": "events.pageCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "log.filter", "control": "tab.filter", "gesture": "click", "payload": { "kind": "Int" } },
              { "eventId": "log.close",  "control": "btn.close",  "gesture": "click" } ],
  "intents": [ { "event": "log.filter", "intent": "ui.setLogFilter", "args": { "kind": "$payload.kind" },
                 "playerSource": "seat", "actorSource": "none" } ]
  // log.close 无意图——纯 UI 事件，编排图消费（G10）
}
```

```text
modal.center（G5）┌──────────────────────────────────┐
                  │ 日志 【全部】【战斗】【✕】        │
                  │ ⚔ 遭遇战 08:12 · ⚒ 铁矿+50 08:11 │
                  └──────────────────────────────────┘
```

30 秒预期：开日志按类型过滤，✕ 关闭。依赖：G5、G8、G9、G10、#1015。

### 分组五 · 编队生产任务（案 22–25）
