#### 案 15：panel.entity.relation —— 实体关系（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；关系边=图内读取。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["⇄ 关系 2", "类型 护卫"]}
```

```jsonc
{
  "id": "panel.entity.relation",
  "graph": "Graph.Entity.Relation",           // 图内读 self 关系边（所属/同盟/敌对）
  "pins": [
    { "name": "relationCount", "key": "relation.count", "mode": "realtime", "default": 0 },
    { "name": "relationKind",  "key": "relation.kind",  "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Entity.Relation（kind: Query）
{
  "id": "Graph.Entity.Relation", "kind": "Query", "entry": "caster",
  "nodes": [
    { "id": "caster", "op": "LoadCaster" },
    { "id": "rels",   "op": "RelationshipQueryIncoming", "relationshipType": "Ally" },
    { "id": "relationCount", "op": "AggCount" },
    { "id": "relationKind",  "op": "ConstInt", "intValue": 1 }
  ],
  "controlEdges": [
    { "from": "caster", "fromPort": "next", "to": "rels" },
    { "from": "rels",   "fromPort": "next", "to": "relationCount" },
    { "from": "relationCount", "fromPort": "next", "to": "relationKind" }
  ],
  "valueEdges": [
    { "from": "caster", "fromPort": "value", "to": "rels", "toPort": "source" },
    { "from": "rels",   "fromPort": "list",  "to": "relationCount", "toPort": "list" }
  ],
  "outputs": [
    { "id": "relationCount", "destination": "Summary", "type": "Int", "source": "relationCount", "key": "relation.count" },
    { "id": "relationKind",  "destination": "Summary", "type": "Int", "source": "relationKind",  "key": "relation.kind" }
  ]
}
```

```text
screen.rightCenter（聚合下方）┌─────────────────────┐
                             │ 所属：王国 A         │
                             │ 同盟：公会 B·敌对：C │
                             └─────────────────────┘
                             （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：选中单位显示所属/同盟/敌对清单。依赖：无。
