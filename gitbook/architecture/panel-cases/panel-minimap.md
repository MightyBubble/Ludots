#### 案 6：panel.minimap —— 小地图（纯展示覆盖层）

> 状态：🟢 今日可装载——纯展示；图内聚合实体位置/阵营输出图层。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "mini", "dots": [[20, 30], [60, 22], [45, 60], [70, 55]]}
```

```jsonc
{
  "id": "panel.minimap",
  "graph": "Graph.Map.Minimap",               // 图内聚合实体位置+阵营 → 输出图层数据
  "pins": [ { "name": "layer", "key": "minimap.layer", "mode": "realtime", "default": 0 } ]
  // 覆盖层画布：皮按 layer 渲染；缺数据=空图层（default 合同）
}
```

```text
screen.bottomLeft ┌──────────┐
                  │ ▦▦  ▦▦▦  │  己方蓝点/敌方红点（皮分层渲染）
                  └──────────┘
```

30 秒预期：兵移动蓝点同步动。依赖：无。
