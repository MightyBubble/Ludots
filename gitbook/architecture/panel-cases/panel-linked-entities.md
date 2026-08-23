#### 案 18：panel.linked.entities —— 关联实体集（纯展示）

> 状态：🔴（配置可装载）——计数 pin 今日可用；成员列表展示依赖 G12（列表型引脚），点击跳转依赖事件声明+#1015。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "⇄ 关联 2 员"}
```

```jsonc
{
  "id": "panel.linked.entities",
  "graph": "Graph.Entity.Linked",             // 图内沿关系边收集关联实体（小队/编队成员）
  "pins": [ { "name": "linkedCount", "key": "linked.count", "mode": "realtime", "default": 0 } ]
}
```

```jsonc
// 值图 Graph.Entity.Linked（kind: Query）
{
  "id": "Graph.Entity.Linked", "kind": "Query", "entry": "caster",
  "nodes": [
    { "id": "caster", "op": "LoadCaster" },
    { "id": "members", "op": "RelationshipQueryOutgoing", "relationshipType": "Squad" },
    { "id": "linkedCount", "op": "AggCount" }
  ],
  "controlEdges": [
    { "from": "caster", "fromPort": "next", "to": "members" },
    { "from": "members", "fromPort": "next", "to": "linkedCount" }
  ],
  "valueEdges": [
    { "from": "caster", "fromPort": "value", "to": "members", "toPort": "source" },
    { "from": "members", "fromPort": "list", "to": "linkedCount", "toPort": "list" }
  ],
  "outputs": [
    { "id": "linkedCount", "destination": "Summary", "type": "Int", "source": "linkedCount", "key": "linked.count" }
  ]
}
```

```text
screen.leftCenter（列表下方）┌──────────────────────┐
                            │ 关联：A 班·B 班        │ 点击跳转属案17 路由机制
                            └──────────────────────┘
                            （目标视觉——G12 列表型引脚后可达；今日 pins 仅驱动标量计数）
```

30 秒预期：选编队队长，关联成员清单列出。依赖：无。

### 分组四 · 信息流（案 19–21）
