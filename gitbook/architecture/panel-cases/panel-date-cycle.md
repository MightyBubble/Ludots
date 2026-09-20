#### 案 2：panel.date.cycle —— 日期（纯展示）

> 状态：🟢 今日可装载——纯展示；年/季/月查表为图内节点。
>
> ⚠️ **基建依赖**：世界日序已由 `CalendarRuntime` 推进并投影（见 [历法与周期](../calendar-system.md)）。面板仍缺 G3（global scope）：`Calendar.DayIndex`/`Calendar.Year`/`Calendar.Season` 还没有全局实体属性出口，值图暂不能 `LoadSelfAttribute`。读日期走 `CalendarRuntime.Project` / `CaptureProgressSnapshot`。日期不进 `Clock.*`。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["第 3 年", "春", "7 日"]}
```

```jsonc
{
  "id": "panel.date.cycle",
  "graph": "Graph.Time.Date",                 // 历法输出 dayIndex；年/季由图内 TableLookup 换算
  "pins": [
    { "name": "dayIndex", "key": "calendar.dayIndex", "mode": "realtime", "default": 1 },
    { "name": "year",     "key": "date.year",      "mode": "realtime", "default": 1 },
    { "name": "season",   "key": "date.season",    "mode": "realtime", "default": 1 }
  ]
}
```

```jsonc
// 值图 Graph.Time.Date（kind: Query）
{
  "id": "Graph.Time.Date", "kind": "Query", "entry": "dayIndex",
  "nodes": [
    { "id": "dayIndex", "op": "LoadSelfAttribute", "attribute": "Calendar.DayIndex" },
    { "id": "year",     "op": "LoadSelfAttribute", "attribute": "Calendar.Year" },
    { "id": "season",   "op": "LoadSelfAttribute", "attribute": "Calendar.Season" }
  ],
  "controlEdges": [
    { "from": "dayIndex", "fromPort": "next", "to": "year" },
    { "from": "year",     "fromPort": "next", "to": "season" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "dayIndex", "destination": "Summary", "type": "Int", "source": "dayIndex", "key": "calendar.dayIndex" },
    { "id": "year",     "destination": "Summary", "type": "Int", "source": "year",     "key": "date.year" },
    { "id": "season",   "destination": "Summary", "type": "Int", "source": "season",   "key": "date.season" }
  ]
}
```

```text
紧贴时间条右侧 ┌──────────────────┐
               │ 第 3 年 · 春 · 7 │   年/季=dayIndex 查周期表（图内节点）
               └──────────────────┘
```

30 秒预期：过夜日期 +1、季节图标换。依赖：G3（global scope 语义）。
