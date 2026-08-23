#### 案 14：panel.entity.aggregate —— 实体信息聚合（纯展示）

> 状态：🟢 今日可装载——纯展示，图内 LoadSelfAttribute 聚合（总合同实体链路）。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "bars", "bars": [["HP", 82], ["MP", 40]]}
```

```jsonc
{
  "id": "panel.entity.aggregate",
  "graph": "Graph.Entity.Aggregate",          // 图内 LoadSelfAttribute 聚合 hp/mp/等级
  "pins": [
    { "name": "hp",    "key": "unit.hp",    "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 },
    { "name": "level", "key": "unit.level", "mode": "realtime", "default": 1 }
  ]
}
```

```text
screen.rightCenter ┌────────────────────┐
                   │ 圣骑士 Lv.6        │ scope=self（CreatePanel source 边传选中实体）
                   │ ▓▓▓▓▓▓░░ 78/100 HP │
                   └────────────────────┘
```

30 秒预期：切换选中单位详情随 scope 换内容。依赖：无。
