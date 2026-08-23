#### 案 10：panel.scene.tint —— 场景染色（全屏效果非框）

> 状态：🟢（装载+链路完整）——归类为全屏效果非框：渲染走皮层全屏覆盖（screen.full 锚点语义待 G5 浮层锚点一并定），非四角框面板。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "🎨 夜幕遮罩 35%（全屏效果）"}
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

```text
全屏覆盖（非框）┌───────────────────────────────────────────┐
               │ ░░ 入夜整屏叠蓝黑（alpha 渐入渐出）░░     │
               └───────────────────────────────────────────┘
```

30 秒预期：入夜场景渐染夜色，天明淡出。依赖：无。
