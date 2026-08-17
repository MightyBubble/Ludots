# gr-op-08 reference · 节点：关系系统

> 现状参考。第一性需求见 [gr-op-08 PRD](../prd/gr-op-08-relationship.md)；配置说明见 [gr-op-08 配置说明](../config/gr-op-08-relationship.md)。

## 1. 现状快照

- 写侧五件（:142-145、:148，Effect）：EnsureLink/RemoveLink（dst=类型符号）、SetMetric/AddMetric/SetFlag（dst=reason，flags=关系类型，imm=度量/旗标符号）——效果组合元数据按 Relationship 域 Unsupported（fail-closed）。
- 读侧三件：GetMetric（:146，LinearAll→Int）、HasFlag（:147，L+Q→Bool）、HasLink（:176，L+Q→Bool，flags=关系类型无 imm）。
- Query 管线 13 件（:149-158、:173-175，QueryOnly）：Outgoing/Incoming/Mutual（source b）/BetweenPair（source b）建集；FilterMetricRange（list source min max）/FilterFlag/SortByMetric（降序旗标）；AggSum/Max/Average/MinMetric→Int；AggMax/MinEntityByMetric→Entity。
- 关系类型/度量/旗标符号来自关系目录（rel-01）；度量聚合为 Int。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 写侧五件描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:142-148 |
| 读侧 GetMetric/HasFlag | GraphOpDescriptorTable.Data.cs:146-147 |
| Query 管线 13 件 | GraphOpDescriptorTable.Data.cs:149-158, 173-175 |
| HasLink | GraphOpDescriptorTable.Data.cs:176 |
| Relationship 域 fail-closed | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs:175-179 |
| 关系目录 | assets/Relationships/catalog.json |

**相关文档**：[gr-op-08 PRD](../prd/gr-op-08-relationship.md) · [gr-op-07 reference](gr-op-07-entityset.md)
