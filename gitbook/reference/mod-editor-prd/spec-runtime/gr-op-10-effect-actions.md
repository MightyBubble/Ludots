# gr-op-10 runtime spec · 节点：效果与事件动作

> 引擎实现任务书。第一性需求见 [gr-op-10 PRD](../prd/gr-op-10-effect-actions.md)；现状见 [reference](../reference/gr-op-10-effect-actions.md)。

## 1. 概述

动作族合同：Effect 事务边界、CallerParams 保留通道、扇出预算、符号四表。

## 2. 设计

- ApplyEffectTemplate 的 a/b 绑定 CallerParams ForceX/Y 通道、RootId 继承调用方——通道语义集中在一处映射，不散落 handler。
- Dynamic 两件的 value 是模板号/预设号：号的世界（配置 id）与符号世界（模板名）分离，节点不做名号互转。
- 扇出统一走 fan-out 命令队列，超预算失败信息带单根计数与上限（事实页）。
- SendEvent 的事件进入帧延迟总线（rt-05），不在图内同步消费。

## 3. 精确语义与不变量

- 九件掩码 = Effect 专属；事务失败动作全回滚。
- ModifyAttributeAdd 走提案/聚合管线；WriteSelfAttribute 不走——两条写路径不合并。
- FanOut 的列表消费一次：动作展开进命令队列后不再引用列表。

## 4. 迁移与治理

现状即基线；无新增治理项。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-10 PRD](../prd/gr-op-10-effect-actions.md) · [reference](../reference/gr-op-10-effect-actions.md)
