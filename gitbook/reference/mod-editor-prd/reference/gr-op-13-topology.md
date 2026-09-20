# gr-op-13 reference · 节点：拓扑谓词

> 现状参考。第一性需求见 [gr-op-13 PRD](../prd/gr-op-13-topology.md)；配置说明见 [gr-op-13 配置说明](../config/gr-op-13-topology.md)。

## 1. 现状快照

- ControlDomainResolve（:188，LinearAll，source→Entity）；ControlDomainControls（:189，a b→Bool）；KnowledgeHasProjection（:190，a b→Bool）。
- 三件纯读、零符号字段；惯用组合 KnowledgeHasProjection.a←LoadViewer（E2）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 三谓词描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:188-190 |

**相关文档**：[gr-op-13 PRD](../prd/gr-op-13-topology.md) · [gr-op-14 reference](gr-op-14-control-flow.md)
