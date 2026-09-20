# tag-03 reference · Tag 变化与事件

> 现状参考。第一性需求见 [tag-03 PRD](../prd/tag-03-changed-events.md)；配置说明见 [tag-03 配置说明](../config/tag-03-changed-events.md)。

## 1. 现状快照

- 变化管道：脏实体队列 → 快照对比（在场/层数/有效缓存）→ 三类变化触发（携旧/新值）→ 事件发布；下一拍分发。
- 事件总线双缓冲（容量见事实页），发布写次缓冲、分拍换入；同帧 EventGate 检查当前缓冲（同帧可见语义）。
- 反应：实体 ReactionBuffer（事件 tag→技能槽，≤8 项），事件到达按槽尝试激活，事件源作显式目标。
- 属性变化走同一管道（幅度=新值）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 反应绑定组件 | src/Core/Gameplay/GAS/Components/ReactionBuffer.cs |
| 脏标记与延迟触发组件 | src/Core/Gameplay/GAS/Components/DeferredTriggerComponents.cs |
| 触发收集/处理系统 | src/Core/Gameplay/GAS/Systems/DeferredTriggerCollectionSystem.cs、DeferredTriggerProcessSystem.cs |
| 脏实体队列 | src/Core/Gameplay/GAS/DirtyEntityQueue.cs |
| 事件总线（双缓冲） | src/Core/Gameplay/GAS/GameplayEventBus.cs |
| 事件分发系统 | src/Core/Gameplay/GAS/Systems/GameplayEventDispatchSystem.cs |
| 反应系统 | src/Core/Gameplay/GAS/Systems/ReactionSystem.cs |
| 容量常量 | src/Core/Gameplay/GAS/GasConstants.cs（见事实页） |

**相关文档**：[tag-03 PRD](../prd/tag-03-changed-events.md) · [tag-01 reference](tag-01-basics.md)
