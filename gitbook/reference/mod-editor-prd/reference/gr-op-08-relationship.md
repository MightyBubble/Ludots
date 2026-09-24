# gr-op-08 reference · 节点：关系系统

> 现状参考。第一性需求见 [gr-op-08 PRD](../prd/gr-op-08-relationship.md)；配置说明见 [gr-op-08 配置说明](../config/gr-op-08-relationship.md)。

## 1. 现状快照

- 建边/断边（EnsureLink/RemoveLink，dst=类型符号）图种为 Effect+Script+TriggerGraph。改度量/改旗标（SetMetric/AddMetric/SetFlag）仍只 Effect。五件写节点的效果组合元数据仍是 Relationship 域 Unsupported（效果计划 fail-closed）。Script/TriggerGraph 的策略单独放行建边与断边，因为这两张图不在效果事务里跑。
- 读侧三件：GetMetric（Int）、HasFlag（Bool）、HasLink（Bool）。线性图、Query（HasFlag/HasLink）、Script、TriggerGraph 可问。
- Query 管线 13 件（QueryOnly）：Outgoing/Incoming/Mutual（source b）/BetweenPair（source b）建集；FilterMetricRange（list source min max）/FilterFlag/SortByMetric（降序旗标）；AggSum/Max/Average/MinMetric→Int；AggMax/MinEntityByMetric→Entity。
- 关系类型/度量/旗标符号来自关系目录（rel-01）；度量聚合为 Int。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 写侧五件描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs |
| 建边/断边的脚本与触发图放行 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.cs |
| Relationship 域 fail-closed | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs |
| 关系目录 | assets/Relationships/catalog.json |

**相关文档**：[gr-op-08 PRD](../prd/gr-op-08-relationship.md) · [gr-op-07 reference](gr-op-07-entityset.md)
