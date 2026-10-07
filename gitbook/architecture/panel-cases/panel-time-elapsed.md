#### 案 1：panel.time.elapsed —— 时间流逝（纯展示走表）

> 状态：🟢 今日可装载——纯展示，图输出 realtime 回读，字段全过白名单（新形状不写 scope）。
>
> ⚠️ **基建依赖**：当天进度千分比与昼夜相位见 [历法与周期](../calendar-system.md)。值图用 `ReadCalendarDayPermille`、`ReadCalendarDayPhase`。`12:34` 是皮层把千分比画成钟面。没有 `Calendar.*` 实体属性。日期不进 `Clock.*`。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["☀", "12:34"]}
```

```jsonc
{
  "id": "panel.time.elapsed",
  "graph": "Graph.Time.Elapsed",              // 值图读当天千分比和昼夜相位
  "pins": [
    { "name": "dayPermille", "key": "calendar.dayPermille", "mode": "realtime", "default": 0 },
    { "name": "dayPhase",    "key": "calendar.dayPhase",    "mode": "realtime", "default": 1 }
  ]
  // 无 events/intents——纯展示；昼夜图标换肤=皮读 dayPhase 自行决定
}
```

```jsonc
// 值图 Graph.Time.Elapsed（kind: Query）
{
  "id": "Graph.Time.Elapsed", "kind": "Query", "entry": "dayPermille",
  "nodes": [
    { "id": "dayPermille", "op": "ReadCalendarDayPermille" },
    { "id": "dayPhase",    "op": "ReadCalendarDayPhase" }
  ],
  "controlEdges": [
    { "from": "dayPermille", "fromPort": "next", "to": "dayPhase" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "dayPermille", "destination": "Summary", "type": "Int", "source": "dayPermille", "key": "calendar.dayPermille" },
    { "id": "dayPhase",    "destination": "Summary", "type": "Int", "source": "dayPhase",    "key": "calendar.dayPhase" }
  ]
}
```

```text
screen.topRight（信息聚合左侧）┌──────────────┐
                              │ ☀ 12:34      │  dayPhase=2 换 ☾（皮层换肤）
                              └──────────────┘
```

30 秒预期：表走字、昼夜图标随相位编号切换。依赖：`Calendar/world.json`。
