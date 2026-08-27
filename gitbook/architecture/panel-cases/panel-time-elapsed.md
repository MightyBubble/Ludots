#### 案 1：panel.time.elapsed —— 时间流逝（纯展示走表）

> 状态：🟢 今日可装载——纯展示，图输出 realtime 回读，字段全过白名单（新形状不写 scope）。
>
> ⚠️ **基建依赖**：当天进度千分比与昼夜相位已由 `CalendarRuntime.CaptureClockSnapshot` 提供（见 [历法与周期](../calendar-system.md)）。`12:34` 是皮层把 `DayPermille` 画成钟面，不是世界钟再算一套分钟。面板仍缺 G3：`Clock.DayPermille`/`Clock.DayPhase` 还没有全局实体属性出口。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["☀", "12:34"]}
```

```jsonc
{
  "id": "panel.time.elapsed",
  "graph": "Graph.Time.Elapsed",              // 时钟图输出 dayPermille/dayPhase
  "pins": [
    { "name": "dayPermille", "key": "clock.dayPermille", "mode": "realtime", "default": 0 },
    { "name": "dayPhase",    "key": "clock.dayPhase",    "mode": "realtime", "default": 1 }
  ]
  // 无 events/intents——纯展示；昼夜图标换肤=皮读 dayPhase 自行决定
}
```

```jsonc
// 值图 Graph.Time.Elapsed（kind: Query）
{
  "id": "Graph.Time.Elapsed", "kind": "Query", "entry": "dayPermille",
  "nodes": [
    { "id": "dayPermille", "op": "LoadSelfAttribute", "attribute": "Clock.DayPermille" },
    { "id": "dayPhase",    "op": "LoadSelfAttribute", "attribute": "Clock.DayPhase" }
  ],
  "controlEdges": [
    { "from": "dayPermille", "fromPort": "next", "to": "dayPhase" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "dayPermille", "destination": "Summary", "type": "Int", "source": "dayPermille", "key": "clock.dayPermille" },
    { "id": "dayPhase",    "destination": "Summary", "type": "Int", "source": "dayPhase",    "key": "clock.dayPhase" }
  ]
}
```

```text
screen.topRight（信息聚合左侧）┌──────────────┐
                              │ ☀ 12:34      │  dayPhase=2 换 ☾（皮层换肤）
                              └──────────────┘
```

30 秒预期：表走字、昼夜图标随 dayPhase 切换。依赖：G3（global scope 语义）。
