# attr-03 reference · 聚合管线

> 现状参考。第一性需求见 [attr-03 PRD](../prd/attr-03-aggregation.md)；配置说明见 [attr-03 配置说明](../config/attr-03-aggregation.md)。

## 1. 现状快照

- AttributeAggregatorSystem 注册于 SystemGroup.AttributeCalculation，先于 AttributeBindingSystem 与 CameraRuntimeSystem；双查询设计：无 DirtyFlags 的 job 直接抛。
- 重算三步：Current=Base 全量复位；遍历 ActiveEffectContainer 效果实体（存活+有 GameplayEffect+未 CancelRequested+State>=Committed+AggregatesModifiers）逐个 ApplyAggregated；派生前快照→执行派生图→derivedWrittenMask。
- Cap 语义：重算后对 DefinedMask 每属性令 Cap=重算 Current；被派生图写过的位不恢复持久 Current（每帧纯重算），其余恢复旧持久值——"聚合改 Cap、直改改 Current"双轨。
- AggregatesModifiers 由效果实体创建时 =(presetType==Buff) 隐式推导，标志存 GameplayEffect.Flags bit 0x10；其它打聚合脏处：效果移除、图内取消、事务 staged 取消、装备授予、效果应用/入栈。
- DirtyFlags=ulong AttributeDirtyMask+TagDirty[32]+DeferredTriggerQueued。Aggregator 对值或 Cap 变化的位打属性脏→MarkDirtyEntity（失败回滚）→表现组件经 CommandBuffer 延迟添加→移除一次性 AttributeAggregateDirty tag。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 系统注册与顺序 | src/Core/Engine/GameEngine.cs:1839-1842 |
| 双查询抛错 | src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs:16-21,264-273 |
| 重算三步 | AttributeAggregatorSystem.cs:124-186 |
| Cap 恢复与派生位豁免 | AttributeAggregatorSystem.cs:275-298 |
| 聚合标志推导与位定义 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1692；src/Core/Gameplay/GAS/Components/GameplayEffect.cs:24,46-60 |
| 效果移除打脏 | src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs:666-673 |
| 图内取消打脏 | src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs:1043-1048 |
| 事务取消打脏 | src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs:722-751 |
| 装备授予打脏 | src/Core/Gameplay/Items/InventoryEquipmentGrantSyncSystem.cs:236 |
| 应用与入栈打脏 | src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs:533-554 |
| DirtyFlags 结构 | src/Core/Gameplay/GAS/Components/DeferredTriggerComponents.cs:42-49 |
| 打脏→表现→清 tag | AttributeAggregatorSystem.cs:220-257,300-315 |

**相关文档**：[attr-03 PRD](../prd/attr-03-aggregation.md) · [attr-02 reference](attr-02-modifiers.md)
