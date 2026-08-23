#### 案 23：panel.quests —— 任务面板（交互）

> 状态：🔴（配置可装载）——运行链路：G8/$payload、G9、#1015、任务条目依赖 G12（列表型引脚）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["2 任务", "追踪 主线"]}
```

```jsonc
{
  "id": "panel.quests",
  "graph": "Graph.Quests",                    // 任务列表+进度输出
  "pins": [ { "name": "questCount", "key": "quests.count", "mode": "realtime", "default": 0 },
            { "name": "activeIndex", "key": "quests.active", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "quest.track", "control": "btn.track", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "quest.track", "intent": "quest.setTracked", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "none" } ]
}
```

```jsonc
// 值图 Graph.Quests（kind: Query）
{
  "id": "Graph.Quests", "kind": "Query", "entry": "questCount",
  "nodes": [
    { "id": "questCount",  "op": "LoadSelfAttribute", "attribute": "Quests.Count" },
    { "id": "activeIndex", "op": "LoadSelfAttribute", "attribute": "Quests.Active" }
  ],
  "controlEdges": [
    { "from": "questCount", "fromPort": "next", "to": "activeIndex" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "questCount",  "destination": "Summary", "type": "Int", "source": "questCount",  "key": "quests.count" },
    { "id": "activeIndex", "destination": "Summary", "type": "Int", "source": "activeIndex", "key": "quests.active" }
  ]
}
```

```text
screen.rightCenter ┌──────────────────────────┐
                   │ ☑ 主线：夺回北门 2/3【追踪】 │ 进度=图内聚合；追踪高亮回读
                   │ ☐ 支线：粮草 完成         │
                   └──────────────────────────┘
                   （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：点追踪切换引导目标。依赖：G8、G9、#1015。
