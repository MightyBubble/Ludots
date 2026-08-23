# gr-op-07 配置说明 · 节点：实体集查询

> 配置写法与行为。第一性需求见 [gr-op-07 PRD](../prd/gr-op-07-entityset.md)；编辑器需求见 [UXD](../uxd/gr-op-07-entityset.md)；现状见 [reference](../reference/gr-op-07-entityset.md)。

## 1. 示例配置

节点画廊真实文件（`QueryFilterTeam.json`，全图取集→队伍过滤；`QueryFromCollection.json`，按集合键取集）：

```json
[
  {
    "id": "showcase.graph_op.QueryFilterTeam",
    "kind": "Query",
    "entry": "all",
    "nodes": [
      { "id": "all", "op": "QueryAllMapEntities" },
      { "id": "team", "op": "QueryFilterTeam", "teamId": 2 }
    ],
    "controlEdges": [
      { "from": "all", "fromPort": "next", "to": "team" }
    ],
    "valueEdges": [
      { "from": "all", "fromPort": "list", "to": "team", "toPort": "list" }
    ]
  }
]
```

```json
[
  {
    "id": "showcase.graph_op.QueryFromCollection",
    "kind": "Query",
    "entry": "caster",
    "nodes": [
      { "id": "caster", "op": "LoadCaster" },
      { "id": "fromCol", "op": "QueryFromCollection", "collectionKey": "squad.members" }
    ],
    "controlEdges": [
      { "from": "caster", "fromPort": "next", "to": "fromCol" }
    ],
    "valueEdges": [
      { "from": "caster", "fromPort": "value", "to": "fromCol", "toPort": "source" }
    ]
  }
]
```

## 2. 逐 op 表

kind 全族为 Q（Query 专属）。TargetList 同 gr-op-06。

| op | 输入引脚 | 输出 | 语义 |
|---|---|---|---|
| QueryAllMapEntities | — | TargetList | 全图实体 |
| QueryFromCollection | source + imm 集合键 | TargetList | 取 source 侧登记的集合 |
| QueryFilterTeam | list teamId | TargetList | 按队伍留（TeamIdSource 旗标定 teamId 来源） |
| QueryFilterTemplate | list + imm 模板 | TargetList | 按实体模板留 |
| QueryFilterAttributeRange | list min max + imm 属性 | TargetList | 属性值落在闭区间 |
| QueryFilterTagAny | list + imm tag | TargetList | 命中任一 tag |
| QueryFilterTagNone | list + imm tag | TargetList | 不命中任何列出 tag |
| QuerySortByAttribute | list + imm 属性 + 降序旗标 | TargetList | 按属性排序（降序可选） |
| AggSumAttribute | list + imm 属性 | Float | 属性求和 |
| AggAverageAttribute | list + imm 属性 | Float | 属性均值 |
| AggMaxAttribute / AggMinAttribute | list + imm 属性 | Float | 属性最值 |
| AggMaxEntityByAttribute / AggMinEntityByAttribute | list + imm 属性 | Entity | 最值对应的实体 |

互斥与陷阱：

- **Query 图是唯一入口**：线性图与 Script 图用不了本族；反过来 gr-op-06 的锥/矩形/线也不进 Query 图——两族互补不重叠。
- AggMax/MinEntityByAttribute 出的是实体不是数值；并列最值取序前者（稳定排序语义），别假设随机。
- 属性区间过滤的 `min`/`max` 是值线不是立即数：要动态区间时接计算节点。
- 队伍过滤与关系过滤（gr-op-08）并存是产品语义：队伍问归属，关系问链路。

## 3. 文件结构

图文档放 `assets/GAS/graphs.json` 或分片目录；`teamId`/`template`/`attribute`/`tag`/`collectionKey` 与排序旗标写在节点字段，见 gr-02。

## 4. 运行时加载效果

属性/tag/模板/集合键符号在编译期经各自注册表解析；执行期建集→过滤→排序→聚合，零分配。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 符号未注册（属性/tag/模板/集合键） | 编译失败，指明节点与符号 |
| 非 Query 图使用 | 编译拒绝 |
| 空列表聚合 | 按空集语义产出，不报错 |

## 6. 实例

- `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QueryAllMapEntities.json`
- 同目录 `QueryFromCollection.json`、`QueryFilterTeam.json`、`QueryFilterTemplate.json`、`QueryFilterAttributeRange.json`、`QueryFilterTagAny.json`、`QueryFilterTagNone.json`、`QuerySortByAttribute.json`、`AggSumAttribute.json`、`AggMaxEntityByAttribute.json` 等

**相关文档**：[gr-op-07 PRD](../prd/gr-op-07-entityset.md) · [gr-op-06 配置说明](gr-op-06-spatial.md) · [gr-op-08 配置说明](gr-op-08-relationship.md)
