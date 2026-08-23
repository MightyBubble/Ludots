#### 案 15：panel.entity.relation —— 实体关系（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；关系边=图内读取。

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

```text
screen.rightCenter（聚合下方）┌─────────────────────┐
                             │ 所属：王国 A         │
                             │ 同盟：公会 B·敌对：C │
                             └─────────────────────┘
```

30 秒预期：选中单位显示所属/同盟/敌对清单。依赖：无。
