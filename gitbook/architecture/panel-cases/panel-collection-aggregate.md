#### 案 16：panel.collection.aggregate —— 集合聚合（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；聚合=图内节点（集合行为图本体随 #1012，缺数据落 default）。

```jsonc
{
  "id": "panel.collection.aggregate",
  "graph": "Graph.Collection.Aggregate",      // 图内聚合集合计数/均值（#1012 集合行为主战场）
  "pins": [
    { "name": "count", "key": "collection.count", "mode": "realtime", "default": 0 },
    { "name": "avgHp", "key": "collection.avgHp", "mode": "realtime", "default": 0 },
    { "name": "cap",   "key": "collection.cap",   "mode": "realtime", "default": 0 }
  ]
}
```

```text
screen.topLeft（tab 下方）┌───────────────────┐
                          │ 部队 24/30 均血 82% │
                          └───────────────────┘
```

30 秒预期：部队增减数字同帧变化。依赖：无。
