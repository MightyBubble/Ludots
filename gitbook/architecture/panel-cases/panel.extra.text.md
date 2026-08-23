#### 案 30：panel.extra.text —— 额外文本（纯展示）

> 状态：🟢 今日可装载——纯展示；snapshot 手动刷新（教程阶段切换时作者主动 Refresh）。

```jsonc
{
  "id": "panel.extra.text",
  "graph": "Graph.Extra.Text",                // 任意文本 id 输出；文案查表=图内节点
  "pins": [ { "name": "textId", "key": "extra.textId", "mode": "snapshot", "default": 0 } ]
}
```

```text
screen.bottomLeft ┌─────────────────────────────┐
                  │ 版本 0.9.2 · 教程：按 U 造兵  │ 锚点由实例 op 覆盖（水印/提示通用）
                  └─────────────────────────────┘
```

30 秒预期：教程阶段切换提示文字更新。依赖：无。
