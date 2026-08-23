#### 案 23：panel.quests —— 任务面板（交互）

> 状态：🔴（配置可装载）——运行链路：G8/$payload、G9、#1015、任务条目依赖 G12（列表型引脚）。

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

```text
screen.rightCenter ┌──────────────────────────┐
                   │ ☑ 守住北门 2/3 【追踪】    │ 进度=图内聚合；追踪高亮回读
                   │ ☐ 招募 10 兵 0/10【追踪】  │
                   └──────────────────────────┘
```

30 秒预期：点追踪切换引导目标。依赖：G8、G9、#1015。
