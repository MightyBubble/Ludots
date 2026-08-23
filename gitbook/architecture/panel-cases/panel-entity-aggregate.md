#### 案 14：panel.entity.aggregate —— 实体信息聚合（纯展示）

> 状态：🟢 今日可装载——纯展示，图内 LoadSelfAttribute 聚合（总合同实体链路）。
>
> ⚠️ **基建依赖**：`Level` 未注册为 GAS 属性（引擎现有注册为 Health/Mana/MoveSpeed/AttackDamage/AttackSpeed 等）——值图引用的 Level 无底层等级系统出口；Health/Mana 本身存在。

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
    { "name": "hpMax", "key": "unit.hpMax", "mode": "realtime", "default": 100 },
    { "name": "mp",    "key": "unit.mp",    "mode": "realtime", "default": 0 },
    { "name": "level", "key": "unit.level", "mode": "realtime", "default": 1 }
  ]
}
```

```jsonc
// 值图 Graph.Entity.Aggregate（kind: Query）
{
  "id": "Graph.Entity.Aggregate", "kind": "Query", "entry": "hp",
  "nodes": [
    { "id": "hp",    "op": "LoadSelfAttribute", "attribute": "Health" },
    { "id": "hpMax", "op": "ConstFloat", "floatValue": 100 },
    { "id": "mp",    "op": "LoadSelfAttribute", "attribute": "Mana" },
    { "id": "level", "op": "LoadSelfAttribute", "attribute": "Level" }
  ],
  "controlEdges": [
    { "from": "hp",    "fromPort": "next", "to": "hpMax" },
    { "from": "hpMax", "fromPort": "next", "to": "mp" },
    { "from": "mp",    "fromPort": "next", "to": "level" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "hp",    "destination": "Summary", "type": "Float", "source": "hp",    "key": "unit.hp" },
    { "id": "hpMax", "destination": "Summary", "type": "Float", "source": "hpMax", "key": "unit.hpMax" },
    { "id": "mp",    "destination": "Summary", "type": "Float", "source": "mp",    "key": "unit.mp" },
    { "id": "level", "destination": "Summary", "type": "Float", "source": "level", "key": "unit.level" }
  ]
}
```

```text
screen.rightCenter ┌────────────────────┐
                   │ 圣骑士 Lv.6        │ scope=self（CreatePanel source 边传选中实体）
                   │ ▓▓▓▓▓▓▓░ 82/100 HP │
                   └────────────────────┘
```

30 秒预期：切换选中单位详情随 scope 换内容。依赖：无。
