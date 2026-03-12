# C7: 窃取技能

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillT"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 6 }  // ability slot
```

### InteractionMode 变体:

| Mode | 行为 |
|------|------|
| TargetFirst | 先选单位, 按键即施放 |
| SmartCast | 按键时取光标下单位, 立即施放 |
| AimCast | 按键进入选取模式, 点击确认 |
| SmartCastWithIndicator | 按住显示范围, 松开施放 |

所有模式已在 `InputOrderMappingSystem` 中实现。

## 实现方案

```
实现:
  1. Target Unit (enemy)
  2. Phase Graph:
     a. 读取 target 最近使用的 ability ID (需要 LastUsedAbility attribute 或 tag)
     b. 将该 ability 注册到 caster 的 AbilityStateBuffer
     c. 替换特定 slot 的 ability
```

- **需要**: `LastUsedAbilityId` 记录机制 + 动态修改 AbilityStateBuffer 的 Graph op

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| AbilityStateBuffer | ✅ 已有 |
| Phase Graph System | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| LastUsedAbilityId 记录 | P2 | 需要在 ability 使用时记录到 attribute 或 Blackboard |
| 动态修改 AbilityStateBuffer Graph op | P2 | 新增 Graph op 用于运行时替换 ability slot |
