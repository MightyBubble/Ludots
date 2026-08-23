# gr-op-09 reference · 节点：聚合与迭代

> 现状参考。第一性需求见 [gr-op-09 PRD](../prd/gr-op-09-aggregate.md)；配置说明见 [gr-op-09 配置说明](../config/gr-op-09-aggregate.md)。

## 1. 现状快照

- AggCount（:113，L+Q+SC，list→Int）；AggMinByDistance（:114，L+Q+SC，list→Entity，距离基准 TargetPos）。
- TargetListGet（:115，L+SC，value 下标→Entity，flags=BoolScratchFlags 有效位；越界→无效句柄+0）。
- 枚举注释明示求值式：E[Dst] = TargetList[I[A]]；B[Flags] = valid（GraphOps.cs:60）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| AggCount / AggMinByDistance / TargetListGet 描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:113-115 |
| 求值式注释 | src/Graph/Ludots.Graph.Abstractions/GraphOps.cs:60 |
| TargetList 容器 | src/Core/NodeLibraries/GASGraph/GraphTargetList.cs |

**相关文档**：[gr-op-09 PRD](../prd/gr-op-09-aggregate.md) · [gr-op-10 reference](gr-op-10-effect-actions.md)
