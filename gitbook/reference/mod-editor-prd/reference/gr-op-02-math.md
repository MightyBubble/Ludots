# gr-op-02 reference · 节点：数学与比较

> 现状参考。第一性需求见 [gr-op-02 PRD](../prd/gr-op-02-math.md)；配置说明见 [gr-op-02 配置说明](../config/gr-op-02-math.md)。

## 1. 现状快照

- Float 侧：Add/Mul/Sub/Div/Min/MaxFloat（a b，LinearAll）、ClampFloat（value min max）、Abs/NegFloat（value）、RandomFloat01、CompareGtFloat（L+SC 出 Bool）。
- Int/实体侧：AddInt、CompareLtInt（L+SC）、CompareEqInt（LinearAll）、CompareEqEntity（L+Q）、SelectEntity（condition a b 出 Entity，LinearAll）。
- LoadAttribute（:86，L+SC，source+属性符号→Float）在数据上属 Float 读源，文档划归 gr-op-04。
- 无 Int↔Float 换算节点；Query 图内本族仅 CompareEqEntity 可用。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 四则与极值 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:87-92 |
| Clamp/Abs/Neg/Random | GraphOpDescriptorTable.Data.cs:93-96 |
| AddInt 与三个比较 | GraphOpDescriptorTable.Data.cs:97-100 |
| CompareEqEntity 与 SelectEntity | GraphOpDescriptorTable.Data.cs:102-103 |

**相关文档**：[gr-op-02 PRD](../prd/gr-op-02-math.md) · [gr-op-04 reference](gr-op-04-attributes.md)
