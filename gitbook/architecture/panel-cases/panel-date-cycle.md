#### 案 2：panel.date.cycle —— 日期（纯展示）

> 状态：🟢 今日可装载——纯展示；年/季/月查表为图内节点。
>
> ⚠️ **基建依赖**：无日历/日期推进系统（引擎时钟仅 `time.scale_permille` + LocalStep 计数）；`Clock.DayIndex`/`Clock.Year`/`Clock.Season` 属性无底层系统。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["第 3 年", "春", "7 日"]}
```

```jsonc
{
  "id": "panel.date.cycle",
  "graph": "Graph.Time.Date",                 // 时钟输出 dayIndex；年/季由图内 TableLookup 换算
  "pins": [
    { "name": "dayIndex", "key": "clock.dayIndex", "mode": "realtime", "default": 1 },
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
    { "id": "dayIndex", "op": "LoadSelfAttribute", "attribute": "Clock.DayIndex" },
    { "id": "year",     "op": "LoadSelfAttribute", "attribute": "Clock.Year" },
    { "id": "season",   "op": "LoadSelfAttribute", "attribute": "Clock.Season" }
  ],
  "controlEdges": [
    { "from": "dayIndex", "fromPort": "next", "to": "year" },
    { "from": "year",     "fromPort": "next", "to": "season" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "dayIndex", "destination": "Summary", "type": "Int", "source": "dayIndex", "key": "clock.dayIndex" },
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
