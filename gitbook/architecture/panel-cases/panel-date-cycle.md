#### 案 2：panel.date.cycle —— 日期（纯展示）

> 状态：🟢 今日可装载——纯展示；年/季/月查表为图内节点。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "第 3 年 · 春 · 7"}
```

```jsonc
{
  "id": "panel.date.cycle",
  "graph": "Graph.Time.Date",                 // 时钟输出 dayIndex；年/季由图内 TableLookup 换算
  "pins": [
    { "name": "dayIndex", "key": "clock.dayIndex", "mode": "realtime", "default": 1 }
  ]
}
```

```text
紧贴时间条右侧 ┌──────────────────┐
               │ 第 3 年 · 春 · 7 │   年/季=dayIndex 查周期表（图内节点）
               └──────────────────┘
```

30 秒预期：过夜日期 +1、季节图标换。依赖：G3（global scope 语义）。
