#### 案 10：panel.scene.tint —— 场景染色（全屏效果非框）

> 状态：🟢（装载+链路完整）——归类为全屏效果非框：渲染走皮层全屏覆盖（screen.full 锚点语义待 G5 浮层锚点一并定），非四角框面板。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["🎨 夜幕", "35%"]}
```

```jsonc
{
  "id": "panel.scene.tint",
  "graph": "Graph.Scene.Tint",                // 输出整屏染色色/透明度（夜幕/中毒/低血）
  "pins": [
    { "name": "tintColor", "key": "scene.tint.color", "mode": "realtime", "default": 0 },
    { "name": "tintAlpha", "key": "scene.tint.alpha", "mode": "realtime", "default": 0 }
  ]
}
```

```jsonc
// 值图 Graph.Scene.Tint（kind: Query）
{
  "id": "Graph.Scene.Tint", "kind": "Query", "entry": "tintColor",
  "nodes": [
    { "id": "tintColor", "op": "ConstInt", "intValue": 0 },
    { "id": "tintAlpha", "op": "LoadSelfAttribute", "attribute": "Scene.Tint.Alpha" }
  ],
  "controlEdges": [
    { "from": "tintColor", "fromPort": "next", "to": "tintAlpha" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "tintColor", "destination": "Summary", "type": "Int",   "source": "tintColor", "key": "scene.tint.color" },
    { "id": "tintAlpha", "destination": "Summary", "type": "Float", "source": "tintAlpha", "key": "scene.tint.alpha" }
  ]
}
```

```text
全屏覆盖（非框）┌───────────────────────────────────────────┐
               │ ░░ 入夜整屏叠蓝黑（alpha 渐入渐出）░░     │
               └───────────────────────────────────────────┘
```

30 秒预期：入夜场景渐染夜色，天明淡出。依赖：无。
