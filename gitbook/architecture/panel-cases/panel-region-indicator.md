#### 案 11：panel.region.indicator —— 区域指示（纯展示）

> 状态：🟢 今日可装载——纯展示；区域归属/威胁=图内读取。
>
> ⚠️ **基建依赖**：`Map.Region.Id`/`Map.Region.Level` 无属性出口——区域基建存在（MapRegionDefinition/RegionTriggerSystem），但区域归属目前只驱动 Trigger 事件，未物化为 entity 属性。

> **高保真预期**（门户面板矩阵页可交互预览）：

```mock
{"type": "stat", "items": ["▣ 北门禁区", "威胁 2"]}
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

```jsonc
// 值图 Graph.Map.Region（kind: Query）
{
  "id": "Graph.Map.Region", "kind": "Query", "entry": "regionId",
  "nodes": [
    { "id": "regionId",    "op": "LoadSelfAttribute", "attribute": "Map.Region.Id" },
    { "id": "regionLevel", "op": "LoadSelfAttribute", "attribute": "Map.Region.Level" }
  ],
  "controlEdges": [
    { "from": "regionId", "fromPort": "next", "to": "regionLevel" }
  ],
  "valueEdges": [],
  "outputs": [
    { "id": "regionId",    "destination": "Summary", "type": "Int", "source": "regionId",    "key": "region.id" },
    { "id": "regionLevel", "destination": "Summary", "type": "Int", "source": "regionLevel", "key": "region.level" }
  ]
}
```

```text
世界锚点（区域边界）┌────────────────────────┐
                    │ ⚠ 危险区（红框）       │ 威胁级决定框色
                    └────────────────────────┘
```

30 秒预期：进危险区边界泛红框，离开消失。依赖：无。
