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
    { "name": "hp",    "key": "unit.hp",    "mode": "realtime", "default": 0 },
    { "name": "hpMax", "key": "unit.hpMax", "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Unit.Status（kind: Query）
{
  "id": "Graph.Unit.Status", "kind": "Query", "entry": "hp",
  "nodes": [
    { "id": "hp",    "op": "LoadSelfAttribute", "attribute": "Health" },
    { "id": "hpMax", "op": "ConstFloat", "floatValue": 100 },
    { "id": "mp",    "op": "LoadSelfAttribute", "attribute": "Mana" }
  ],
  "controlEdges": [
    { "from": "hp", "fromPort": "next", "to": "hpMax" },
    { "from": "hpMax", "fromPort": "next", "to": "mp" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "hp",    "destination": "Summary", "type": "Float", "source": "hp",    "key": "unit.hp" },
    { "id": "hpMax", "destination": "Summary", "type": "Float", "source": "hpMax", "key": "unit.hpMax" },
    { "id": "mp",    "destination": "Summary", "type": "Float", "source": "mp",    "key": "unit.mp" }
  ]
}
```

```text
世界锚点（单位头顶）┌───────────────┐
                    │ ▓▓▓▓▓░░ 72  │ 血条溢出屏外自动收起（皮读 hp/hpMax 自比）
                    └───────────────┘
```

30 秒预期：单位挨打血条变短。依赖：无。
