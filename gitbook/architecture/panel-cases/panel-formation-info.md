#### 案 22：panel.formation.info —— 编队信息（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；编队聚合=图内节点。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["楔形阵", "12 员"]}
```

```jsonc
{
  "id": "panel.formation.info",
  "graph": "Graph.Formation.Info",            // 图内聚合编队成员属性 → 阵型/均速/士气
  "pins": [
    { "name": "formationKind", "key": "formation.kind", "mode": "realtime", "default": 0 },
    { "name": "avgSpeed",      "key": "formation.avgSpeed", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Formation.Info（kind: Query）
{
  "id": "Graph.Formation.Info", "kind": "Query", "entry": "formation",
  "nodes": [
    { "id": "formation",     "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 300 },
    { "id": "formationKind", "op": "ConstInt", "intValue": 0 },
    { "id": "avgSpeed",      "op": "AggAverageAttribute", "attribute": "MoveSpeed" }
  ],
  "controlEdges": [
    { "from": "formation",     "fromPort": "next", "to": "formationKind" },
    { "from": "formationKind", "fromPort": "next", "to": "avgSpeed" }
  ],
  "valueEdges": [
    { "from": "formation", "fromPort": "list", "to": "avgSpeed", "toPort": "list" }
  ],
  "outputs": [
    { "id": "formationKind", "destination": "Summary", "type": "Int",   "source": "formationKind", "key": "formation.kind" },
    { "id": "avgSpeed",      "destination": "Summary", "type": "Float", "source": "avgSpeed",      "key": "formation.avgSpeed" }
  ]
}
```

```text
screen.bottomLeft 上方 ┌─────────────────────────┐
                       │ 楔形阵 均速 3.2 士气 80  │ 阵型名查表=图内节点
                       └─────────────────────────┘
```

30 秒预期：切换阵型信息条同步更新。依赖：无。
