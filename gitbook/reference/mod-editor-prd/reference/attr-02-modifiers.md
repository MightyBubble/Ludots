# attr-02 reference · 修改器

> 现状参考。第一性需求见 [attr-02 PRD](../prd/attr-02-modifiers.md)；配置说明见 [attr-02 配置说明](../config/attr-02-modifiers.md)。

## 1. 现状快照

- ModifierOp 三值 byte 枚举：Add=0、Multiply=1、Override=2。EffectModifiers 定长三段数组+Count，容量见事实页；Add 满容静默返回 false，EffectTemplateLoader 不检查返回值——第 9 条静默丢失（configParams 溢出则抛错）。
- 即时路径 Apply：clampToCapacity=true，逐条顺序执行（Add 加、Multiply 乘、Override 覆盖），SetCurrent 带钳制——clampToBase 时上限取 GetBase，而 GetBase 在该模式下返回 CapValues，即"上限=当前聚合 Cap"。
- 聚合路径 ApplyAggregated：clampToCapacity=false，走 SetAggregatedCurrent 绕过 ClampCurrentToBase 上限。SetCurrentInternal 无条件置 DefinedMask 位；SetBase 同时重置 Cap 与 Current。
- 写入权威 AttributeMutationOps 五入口：AddCurrent/SetCurrent/ReplaceCurrentFromCap/SetBase/ApplyModifiers。统一管道：要求 DirtyFlags（缺失抛）→ 快照 before → 值不变早退 → 属性脏+实体脏+（有 ActiveEffectContainer 时）聚合脏 → 表现位；异常手动回滚 buffer+flags。ApplyModifiers 以 touchedMask 去重、逐属性 diff 打脏。
- 即时落点两处同构二分（提案处理系统与图内 BuiltinHandlers）：事务 active 先 StageModifiers 进事务缓冲，否则直执行；提交对 changedMask 逐属性 SetBase/SetCurrent 回写。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| ModifierOp 枚举 | src/Core/Gameplay/GAS/Components/EffectModifiers.cs:5-10 |
| 定长数组与容量 | EffectModifiers.cs:23-39；src/Core/Gameplay/GAS/GasConstants.cs:46-47 |
| 即时与聚合执行 | src/Core/Gameplay/GAS/EffectModifierOps.cs:17-73 |
| 钳制与 DefinedMask | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs:31-41,53-60,83-98 |
| 写入权威 | src/Core/Gameplay/GAS/AttributeMutationOps.cs:12-226 |
| 静默丢条 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:267-271（对照 :1336-1339） |
| 即时落点二分 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1547-1577 |
| 图内落点二分 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:85-119 |
| 事务暂存与提交回写 | src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs:379-387,871-899 |

**相关文档**：[attr-02 PRD](../prd/attr-02-modifiers.md) · [attr-03 reference](attr-03-aggregation.md)
