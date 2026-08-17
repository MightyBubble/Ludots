# gr-op-03 reference · 节点：标签

> 现状参考。第一性需求见 [gr-op-03 PRD](../prd/gr-op-03-tags.md)；配置说明见 [gr-op-03 配置说明](../config/gr-op-03-tags.md)。

## 1. 现状快照

- 本族现存仅 HasTag：source+tag 符号→Bool，掩码 LinearQueryScript（六类通用），判定走 Effective 有效集。
- SelectTagInMask 与 LookupTagDisplayToken 已随 TagDisplay 专线删除（ADR #876）；全库无此二 op，仅表现层 TagDisplayTable 残名。
- "纯读选 tag id"节点空档：ADR 决策表留有"可另单保留"活口，无实现（见 TODO T8/G8）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| HasTag 描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:101 |
| op 枚举（无已删二 op） | src/Graph/Ludots.Graph.Abstractions/GraphOps.cs |
| 表现层残名 | TagDisplayTable（表现层，无图节点对应） |

**相关文档**：[gr-op-03 PRD](../prd/gr-op-03-tags.md) · [tag-01 reference](tag-01-basics.md)
