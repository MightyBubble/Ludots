---
文档类型: 测试用例
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: Claude
文档版本: v1.0
适用范围: GAS - 功能空白深度验证 (GasFeatureGapTests.cs)
状态: 已实现
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/06_Effect生命周期Phase_架构设计.md
---

# GAS 功能空白深度验证测试用例

## 1 测试总览

| 维度 | 数量 |
|------|------|
| 测试文件 | `src/Tests/GasTests/GasFeatureGapTests.cs` |
| 测试方法 | 18 |
| 覆盖分类 | GasClock, ExpireCondition, DeferredTrigger, ResponseChain, TeamManager, EffectStack, GrantedTags, AbilityCost, EffectModifiers, GasConditionRegistry, TagOps |

## 2 测试方法清单

### 2.1 GasClock Manual/TurnBased

| 测试方法 | 验证内容 |
|----------|----------|
| `GasClock_ManualMode_DoesNotAdvanceWithoutRequest` | Manual 模式下不请求 = Step 不推进 |
| `GasClock_ManualMode_AdvancesOnRequest` | RequestStep(3) = 精确推进 3 步 |
| `GasClock_PausedMode_NeverAdvances` | Paused 模式完全冻结 |
| `GasClock_SwitchAutoToManual_StopsAutoAdvancement` | 运行时切换模式, 验证行为变更 |

### 2.2 Effect ExpireCondition (TagPresent / TagAbsent)

| 测试方法 | 验证内容 |
|----------|----------|
| `ExpireCondition_TagPresent_EffectExpiresWhenTagRemoved` | TagPresent 条件: 标签移除时效果过期 |
| `ExpireCondition_TagAbsent_EffectExpiresWhenTagAdded` | TagAbsent 条件: 标签添加时效果过期 |

### 2.3 DeferredTrigger

| 测试方法 | 验证内容 |
|----------|----------|
| `DeferredTrigger_TagChanged_DirtyFlagMarksCorrectTag` | 标签脏标记位图正确性 |
| `DeferredTrigger_AttributeChanged_TracksOldAndNewValue` | 属性快照 OldValue/NewValue 正确采集 |

### 2.4 Response Chain

| 测试方法 | 验证内容 |
|----------|----------|
| `ResponseChainListener_ChainType_StoresEffectTemplateId` | Chain 类型存储追加效果模板 ID |
| `ResponseChainListener_MultipleTypes_CoexistInSameListener` | 同一 Listener 多类型(Hook/Modify/Chain)共存 |
| `ResponseChainListener_Capacity_RejectsOverflow` | 超过 CAPACITY(8) 时拒绝 |

### 2.5 TeamManager

| 测试方法 | 验证内容 |
|----------|----------|
| `TeamManager_AsymmetricRelationship_AViewsDifferentlyThanB` | 非对称: A→B=Hostile, B→A=Friendly |
| `TeamManager_ThreeFactionSetup_AllPairsCorrect` | 3 阵营全 Hostile + 同队=Friendly |
| `TeamManager_FourFactionAsymmetric_ComplexDiplomacy` | 4 阵营复杂外交 (Alliance/War/Neutral + 单向) |

### 2.6 其他组件

| 测试方法 | 验证内容 |
|----------|----------|
| `EffectStack_AddDuration_Policy` | AddDuration 策略正确 |
| `EffectStack_KeepDuration_DoesNotAffectDuration` | KeepDuration 策略 |
| `EffectGrantedTags_MultipleEntries_CorrectStorage` | 多条 GrantedTags 存储和检索 |
| `AbilityCost_MultiResource_CheckBothBeforeDeducting` | 多资源消耗原子性检查 |
| `EffectModifiers_MultipleOps_AllApplied` | 多属性多操作修改器 |
| `GasConditionRegistry_RegisterAndLookup` | 条件注册表注册+查询 round-trip |
| `TagOps_AttachedTag_AutomaticallyAdded` | Attached 规则自动添加 |
| `TagOps_RemovedTag_AutomaticallyRemoved` | Removed 规则自动移除 |
