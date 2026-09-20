# gr-op-12 reference · 节点：放置校验

> 现状参考。第一性需求见 [gr-op-12 PRD](../prd/gr-op-12-placement.md)；配置说明见 [gr-op-12 配置说明](../config/gr-op-12-placement.md)。

## 1. 现状快照

- ClampTargetToRange（:181，LinearAll，a b→Bool，原地改 TargetPos）；IsPointInCircle（:182，a b→Bool 纯谓词）。
- SnapToNearestInCollection（:183，source value+集合键→Entity，flags=BoolScratchFlags 可带 `validOutput` 命名有效口）；SnapToNearestGraphEdge（:184，value→Bool，经 GraphEdgeProjectionQuery）。
- 集合键经 ConfigKeyRegistry（与黑板键同池）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Clamp 与圈判定 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:181-182 |
| 两件吸附 | GraphOpDescriptorTable.Data.cs:183-184 |
| 图边投影查询 | GraphEdgeProjectionQuery（图边投影通道） |

**相关文档**：[gr-op-12 PRD](../prd/gr-op-12-placement.md) · [gr-op-13 reference](gr-op-13-topology.md)
