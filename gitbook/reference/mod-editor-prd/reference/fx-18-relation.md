# fx-18 reference · 关系操作

> 现状参考。第一性需求见 [fx-18 PRD](../prd/fx-18-relation.md)；配置说明见 [fx-18 配置说明](../config/fx-18-relation.md)。

## 1. 现状快照

- loader：relation 块仅 Relation preset + Instant；operation 三值；subject 禁 None，SetParent/EnsureLink 要求 parent 非 None；snap 仅 SetParent；relationshipType 仅 EnsureLink 且需 RelationshipTypeRegistry（未注册报错）。
- runtime：HandleApplyRelation 事务内仅支持 SetParent（StageSetParent + 可选吸附）；RemoveParent/EnsureLink 走直改世界（RelationOps.RemoveParent / Relationships.EnsureLink）。
- 计划编译：RemoveParent/EnsureLink 的操作元数据为 Unsupported(Relationship)，FinalizeAll 编译即抛 `GAS.EFFECT_PLAN.ERR.UnsupportedOperation`——配置可写、启动不可用；测试固化该行为。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:367-384 |
| relation 编译与槽位校验 | EffectTemplateLoader.cs:606-686 |
| 操作解析 | EffectTemplateLoader.cs:1093-1108 |
| 关系处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:515-599 |
| 按操作分级元数据 | BuiltinHandlers.cs:601-617 |
| Unsupported fail-closed | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:600-603 |
| 行为固化测试 | src/Tests/GasTests/Effect/EffectExecutionPlanTests.cs:133-160 |

**相关文档**：[fx-18 PRD](../prd/fx-18-relation.md) · [fx-18 配置说明](../config/fx-18-relation.md)
