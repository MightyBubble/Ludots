# gr-op-07 reference · 节点：实体集查询

> 现状参考。第一性需求见 [gr-op-07 PRD](../prd/gr-op-07-entityset.md)；配置说明见 [gr-op-07 配置说明](../config/gr-op-07-entityset.md)。

## 1. 现状快照

- 全族 QueryOnly 14 件：QueryAllMapEntities（:159）、QueryFromCollection（:160，source+集合键）；过滤 Team（:161，list+teamId，TeamIdSource 旗标）/Template（:162）/AttributeRange（:163，list+min+max）/TagAny（:164）/TagNone（:165）；QuerySortByAttribute（:166，降序旗标）；Agg Sum/Average/Max/MinAttribute（:167-170，→Float）；AggMax/MinEntityByAttribute（:171-172，→Entity）。
- QueryFilterTeam 现用 opcode 为重立值（原 110 位注释见 gr-op-06 的 G9）；与 QueryFilterRelationship 并存。
- 属性/tag/模板/集合键均编译期符号解析。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 建集两件 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:159-160 |
| 过滤五件 | GraphOpDescriptorTable.Data.cs:161-165 |
| 排序 | GraphOpDescriptorTable.Data.cs:166 |
| 属性聚合四件 | GraphOpDescriptorTable.Data.cs:167-170 |
| 实体聚合两件 | GraphOpDescriptorTable.Data.cs:171-172 |

**相关文档**：[gr-op-07 PRD](../prd/gr-op-07-entityset.md) · [gr-op-08 reference](gr-op-08-relationship.md)
