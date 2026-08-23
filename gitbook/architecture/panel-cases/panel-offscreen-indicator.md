#### 案 9：panel.offscreen.indicator —— 屏外指示（纯展示）

> 状态：🟢 今日可装载——纯展示；屏外投影=图内节点。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "chips", "chips": ["↖ 敌", "↗ 援", "↘ 集"], "on": -1}
```

```jsonc
{
  "id": "panel.offscreen.indicator",
  "graph": "Graph.Map.Offscreen",             // 图内投影屏外目标方向/距离
  "pins": [
    { "name": "dir",  "key": "offscreen.dir",  "mode": "realtime", "default": 0 },
    { "name": "dist", "key": "offscreen.dist", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Map.Offscreen（kind: Query）
{
  "id": "Graph.Map.Offscreen", "kind": "Query", "entry": "dir",
  "nodes": [
    { "id": "dir",  "op": "ConstFloat", "floatValue": 45 },
    { "id": "dist", "op": "ConstFloat", "floatValue": 120 }
  ],
  "controlEdges": [
    { "from": "dir", "fromPort": "next", "to": "dist" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "dir",  "destination": "Summary", "type": "Float", "source": "dir",  "key": "offscreen.dir" },
    { "id": "dist", "destination": "Summary", "type": "Float", "source": "dist", "key": "offscreen.dist" }
  ]
}
```

```text
屏幕边缘（世界锚点投影）┌──────────────────────────────┐
                        │        ↗ 敌军编队 120m       │ 箭头贴边旋转指向
                        └──────────────────────────────┘
```

30 秒预期：敌军移出屏幕，边缘箭头指向其方位。依赖：无。
