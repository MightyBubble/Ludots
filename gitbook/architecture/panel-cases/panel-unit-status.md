#### 案 26：panel.unit.status —— 状态条（纯展示）

> 状态：🟢 今日可装载——纯展示；图内 LoadSelfAttribute 聚合。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "bars", "bars": [["HP", 72], ["MP", 55]]}
```

```jsonc
{
  "id": "panel.unit.status",
  "graph": "Graph.Unit.Status",               // 图内 LoadSelfAttribute → hp/hpMax/mp
  "pins": [
    { "name": "hp",    "key": "unit.hp",    "mode": "realtime", "default": 100 },
    { "name": "hpMax", "key": "unit.hpMax", "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（单位头顶）┌───────────────┐
                    │ ▓▓▓▓▓░░ 78  │ 血条溢出屏外自动收起（皮读 hp/hpMax 自比）
                    └───────────────┘
```

30 秒预期：单位挨打血条变短。依赖：无。
