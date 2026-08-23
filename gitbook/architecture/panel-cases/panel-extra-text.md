#### 案 30：panel.extra.text —— 额外文本（纯展示）

> 状态：🟢 今日可装载——纯展示；snapshot 手动刷新（教程阶段切换时作者主动 Refresh）。
>
> ⚠️ **基建依赖**：`Extra.TextId` 无底层系统——设计为作者手动 Refresh 的占位文本源，无稳定数据出口。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "“北风呼啸……”"}
```

```jsonc
{
  "id": "panel.extra.text",
  "graph": "Graph.Extra.Text",                // 任意文本 id 输出；文案查表=图内节点
  "pins": [ { "name": "textId", "key": "extra.textId", "mode": "snapshot", "default": 0 } ]
}
```

```jsonc
// 值图 Graph.Extra.Text（kind: Query）
{
  "id": "Graph.Extra.Text", "kind": "Query", "entry": "textId",
  "nodes": [
    { "id": "textId", "op": "LoadSelfAttribute", "attribute": "Extra.TextId" }
  ],
  "controlEdges": [],
  "valueEdges": [],
  "outputs": [
    { "id": "textId", "destination": "Summary", "type": "Int", "source": "textId", "key": "extra.textId" }
  ]
}
```

```text
screen.bottomLeft ┌─────────────────────────────┐
                  │ 版本 0.9.2 · 教程：按 U 造兵  │ 锚点由实例 op 覆盖（水印/提示通用）
                  └─────────────────────────────┘
```

30 秒预期：教程阶段切换提示文字更新。依赖：无。
