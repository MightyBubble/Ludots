# gr-op-05 reference · 节点：黑板

> 现状参考。第一性需求见 [gr-op-05 PRD](../prd/gr-op-05-blackboard.md)；配置说明见 [gr-op-05 配置说明](../config/gr-op-05-blackboard.md)。

## 1. 现状快照

- Read Float/Int/Entity（:128-130，L+SC，source+键符号）；Write Float/Int/Entity（:131-133，Effect，source+value+键符号）。
- 键经 ConfigKeyRegistry；黑板条目上限见事实页（GasConstants）。
- 图内键与订单黑板同池；订单内置键见 ord-04。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Read 三件 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:128-130 |
| Write 三件 | GraphOpDescriptorTable.Data.cs:131-133 |
| 键注册表 | src/Core/Gameplay/GAS/Registry/ConfigKeyRegistry.cs:5 |

**相关文档**：[gr-op-05 PRD](../prd/gr-op-05-blackboard.md) · [gr-op-04 reference](gr-op-04-attributes.md)
