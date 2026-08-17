# gr-op-06 配置说明 · 节点：空间查询

> 配置写法与行为。第一性需求见 [gr-op-06 PRD](../prd/gr-op-06-spatial.md)；编辑器需求见 [UXD](../uxd/gr-op-06-spatial.md)；现状见 [reference](../reference/gr-op-06-spatial.md)。

## 1. 示例配置

节点画廊真实文件（`QueryLimit.json`，圆查询→稳定排序→截断前三）：

```json
[
  {
    "id": "showcase.graph_op.QueryLimit",
    "kind": "Query",
    "entry": "radius",
    "nodes": [
      { "id": "radius", "op": "QueryRadius", "queryCapacityPolicy": "RequireComplete", "radiusCm": 800 },
      { "id": "sort", "op": "QuerySortStable" },
      { "id": "limit", "op": "QueryLimit", "intValue": 3 }
    ],
    "controlEdges": [
      { "from": "radius", "fromPort": "next", "to": "sort" },
      { "from": "sort", "fromPort": "next", "to": "limit" }
    ],
    "valueEdges": [
      { "from": "radius", "fromPort": "list", "to": "sort", "toPort": "list" },
      { "from": "sort", "fromPort": "list", "to": "limit", "toPort": "list" }
    ]
  }
]
```

线性图里的锥查询管线（`QueryFilterLayer.json` 摘要）：`QueryCone` 带 `queryCapacityPolicy` 与 `rangeCm`，`a`/`b` 值线接 ConstInt（朝向 90、半角 30），后接 `QueryFilterNotEntity`（source 接 LoadCaster）与 `QueryFilterLayer`（`layerMask: 2`）。六边形（`QueryHexRange.json`）：`hexRadius: 2` 立即数。

## 2. 逐 op 表

kind 缩写同 gr-op-01。TargetList 指查询管线的目标列表值线。

| op | 可用 kind | 输入引脚 | 输出 | 语义 |
|---|---|---|---|---|
| QueryRadius | L+Q+SC | imm 半径 | TargetList | 圆查询，`radiusCm` |
| QueryCone | L | a b | TargetList | 锥查询，中心偏施法者 |
| QueryRectangle | L | a b | TargetList | 矩形查询，中心偏施法者 |
| QueryLine | L | a b | TargetList | 线查询，中心偏施法者 |
| QueryHexRange | L | imm | TargetList | 六边范围，`hexRadius` |
| QueryHexRing | L | imm | TargetList | 六边环 |
| QueryHexNeighbors | L | — | TargetList | 六邻域 |
| QuerySortStable | L+Q+SC | list | TargetList | 稳定排序（距离） |
| QueryLimit | L+Q+SC | list + imm | TargetList | 截断到前 N |
| QueryFilterNotEntity | L | source | TargetList | 管线中剔除 source 实体 |
| QueryFilterLayer | L | imm 层掩码 | TargetList | 只留命中层，`layerMask` |
| QueryFilterRelationship | L | source + imm | TargetList | 按关系类型留/剔，接 source 判关系 |

全部形状查询节点带 `queryCapacityPolicy`（SpatialCapacityFlags）：`RequireComplete` 或 `AllowTruncated`。

互斥与陷阱：

- kind 覆盖不一致：QueryRadius/Sort/Limit 可进 Query 与 Script 图；锥/矩形/线、六边三件、三个 Filter 只在线性四类图（Effect/Score/Validation/Derived）。
- 查询中心规则分两派：锥/线/矩形偏施法者；圆与六边系先取目标点（无则回退施法者）。以为"都以目标为中心"会在 Validation 图里圈错人。
- `AllowTruncated` 不报错但会丢命中：治疗全队的图误用截断会静默少奶人，读 dropped 计数确认。
- QueryFilterRelationship 与 gr-op-08 的关系过滤语义同名不同族：此处是空间管线内按关系收窄，标签/关系目录见 rel-01。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；形状参数（`radiusCm`/`rangeCm`/`hexRadius`/`layerMask`/`intValue`）与 `queryCapacityPolicy` 写在节点字段，见 gr-02。

## 4. 运行时加载效果

编译期按 kind 校验掩码与引脚；执行期形状查询走空间检索填 TargetList，流水线节点原位收窄，全程零分配（容量上限见事实页）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| RequireComplete 装不下命中集 | 执行失败并报容量 |
| AllowTruncated 截断 | 不报错，dropped 计数可查 |
| 形状引脚悬空 / kind 越界 | 编译失败 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryRadius.json`
- 同目录 `QueryCone.json`、`QueryRectangle.json`、`QueryLine.json`、`QueryHexRange.json`、`QueryHexRing.json`、`QueryHexNeighbors.json`、`QuerySortStable.json`、`QueryLimit.json`、`QueryFilterNotEntity.json`、`QueryFilterLayer.json`、`QueryFilterRelationship.json`

**相关文档**：[gr-op-06 PRD](../prd/gr-op-06-spatial.md) · [gr-op-09 配置说明](gr-op-09-aggregate.md) · [fx-09 配置说明](fx-09-target-query.md)
