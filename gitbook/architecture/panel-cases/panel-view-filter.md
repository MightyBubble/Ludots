#### 案 29：panel.view.filter —— 视图过滤器（交互）

> 状态：🔴（配置可装载）——运行链路：G8/$payload、G9、#1015、任务条目依赖 G12（列表型引脚）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["军事", "经济", "外交"], "on": 0}
```

```jsonc
{
  "id": "panel.view.filter",
  "graph": "Graph.View.Filter",               // 当前过滤条件回读
  "pins": [ { "name": "filterState", "key": "view.filterState", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "filter.toggle", "control": "bar.filter", "gesture": "click", "payload": { "flag": "Int" } } ],
  "intents": [ { "event": "filter.toggle", "intent": "view.toggleFilter", "args": { "flag": "$payload.flag" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```text
screen.topLeft（tab 下方）┌─────────────────────────────┐
                          │【兵】【建】【资源】【敌方】    │ 高亮=filterState 回读
                          └─────────────────────────────┘
```

30 秒预期：点"敌方"隐藏敌方单位，再点恢复。依赖：G8、G9、#1015。
