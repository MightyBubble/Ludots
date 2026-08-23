#### 案 28：panel.loadout —— 物品装备（交互）

> 状态：🔴 目标态——拒因：G8（$payload）+ #1015。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "list", "rows": [["武器", "铁剑 ATK+5"], ["护甲", "皮甲 HP+20"]]}
```

```jsonc
{
  "id": "panel.loadout",
  "graph": "Graph.Unit.Loadout",              // 装备槽位+物品属性输出
  "pins": [ { "name": "slotCount", "key": "loadout.slotCount", "mode": "realtime", "default": 0 } ],
  "events": [ { "eventId": "loadout.equip", "control": "grid.loadout", "gesture": "click", "payload": { "slot": "Int" } } ],
  "intents": [ { "event": "loadout.equip", "intent": "unit.equipSlot", "args": { "slot": "$payload.slot" },
                 "playerSource": "seat", "actorSource": "commandSource.primary" } ]
}
```

```jsonc
// 值图 Graph.Unit.Loadout（kind: Query）
{
  "id": "Graph.Unit.Loadout", "kind": "Query", "entry": "slotCount",
  "nodes": [
    { "id": "slotCount", "op": "LoadSelfAttribute", "attribute": "Loadout.SlotCount" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "slotCount", "destination": "Summary", "type": "Int", "source": "slotCount", "key": "loadout.slotCount" }
  ]
}
```

```text
screen.rightCenter（聚合下方）┌────────────────────────┐
                             │ 武器 铁剑 ATK+5 │ 点击槽位=装备/卸下（携带物清单在图内）
                             │ 护甲 皮甲 HP+20 │
                             └────────────────────────┘
```

30 秒预期：点槽位换装备，属性回读更新。依赖：G8、#1015。

### 分组七 · 其他（案 29–31）
