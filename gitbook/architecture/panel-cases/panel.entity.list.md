#### 案 13：panel.entity.list —— 实体列表（交互选中）

> 状态：🔴（配置可装载）——运行链路：G8/$payload、#1015、条目列表依赖 G12（列表型引脚）。

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",               // 图内过滤+排序集合 → 行数据
  "pins": [ { "name": "rowCount", "key": "list.rowCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "entity.pick", "control": "list.entities", "gesture": "click", "payload": { "row": "Int" } } ],
  "intents": [ { "event": "entity.pick", "intent": "selection.setTarget", "args": { "row": "$payload.row" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```text
screen.leftCenter ┌──────────────────────┐
                  │ ▸ 步兵班 A   8/8     │ 点击行→选中对应实体（行→实体映射在图内）
                  │   弓手班 B   5/6     │
                  └──────────────────────┘
```

30 秒预期：点列表行对应单位被选中并高亮。依赖：G8、#1015。
