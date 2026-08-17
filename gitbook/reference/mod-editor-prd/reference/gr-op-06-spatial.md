# gr-op-06 reference · 节点：空间查询

> 现状参考。第一性需求见 [gr-op-06 PRD](../prd/gr-op-06-spatial.md)；配置说明见 [gr-op-06 配置说明](../config/gr-op-06-spatial.md)。

## 1. 现状快照

- 七形状：QueryRadius（:104，L+Q+SC，imm 半径）；Cone/Rectangle/Line（:107-109，LinearAll，a b）；HexRange/Ring/Neighbors（:116-118，LinearAll，Hex 前两件 imm）。全部带 SpatialCapacityFlags（Neighbors 除外，:118 无 flags 行参）。
- 管线五件：QuerySortStable（:105）、QueryLimit（:106，imm）为 L+Q+SC；FilterNotEntity（:110，source）、FilterLayer（:111，imm=LayerMask）、FilterRelationship（:112，source+imm）为 LinearAll。
- 中心解析：锥/线/矩形 preferSourceCenter，其余先目标点再施法者兜底。
- opcode 101（QueryFilterTagAll）与 110 注释史：101 注释在案；110 的"已删 QueryFilterTeam"注释失真——QueryFilterTeam 现以新 opcode 存在（见 gr-op-07）。
- TargetList 容量（MaxTargets）见 GraphVmLimits 与事实页。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| QueryRadius 与管线两件 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:104-106 |
| Cone/Rectangle/Line | GraphOpDescriptorTable.Data.cs:107-109 |
| Filter 三件 | GraphOpDescriptorTable.Data.cs:110-112 |
| Hex 三件 | GraphOpDescriptorTable.Data.cs:116-118 |
| 中心解析两派 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:132-155 |
| 死注释（110 removed） | src/Graph/Ludots.Graph.Abstractions/GraphOps.cs:52 |
| 容量上限 | src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs:9 |

**相关文档**：[gr-op-06 PRD](../prd/gr-op-06-spatial.md) · [gr-op-07 reference](gr-op-07-entityset.md)
