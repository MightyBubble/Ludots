#### 案 11：panel.region.indicator —— 区域指示（纯展示）

> 状态：🟢 今日可装载——纯展示；区域归属/威胁=图内读取。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "text", "text": "▣ 北门禁区 · 剩余 02:31"}
```

```jsonc
{
  "id": "panel.region.indicator",
  "graph": "Graph.Map.Region",                // 图内读区域归属/威胁等级；区域名查表=图内节点
  "pins": [
    { "name": "regionId",    "key": "region.id",    "mode": "realtime", "default": 0 },
    { "name": "regionLevel", "key": "region.level", "mode": "realtime", "default": 0 }
  ]
}
```

```text
世界锚点（区域边界）┌────────────────────────┐
                    │ ⚠ 危险区（红框）       │ 威胁级决定框色
                    └────────────────────────┘
```

30 秒预期：进危险区边界泛红框，离开消失。依赖：无。
