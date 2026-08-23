#### 案 22：panel.formation.info —— 编队信息（纯展示）

> 状态：🔴（配置可装载）——计数/标量 pin 可用；条目内容依赖 G12（列表型引脚）；纯展示；编队聚合=图内节点。

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

```text
screen.bottomLeft 上方 ┌─────────────────────────┐
                       │ 楔形阵 均速 3.2 士气 80  │ 阵型名查表=图内节点
                       └─────────────────────────┘
```

30 秒预期：切换阵型信息条同步更新。依赖：无。
