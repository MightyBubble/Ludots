# C6: 操控自己的召唤物

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillF"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 5 }  // ability slot
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
  1. 召唤物有 "summoned" tag + owner entity ref (Blackboard)
  2. SelectionRule: filter=OwnedSummon (custom)
  3. Phase Graph: 通过 Blackboard 给召唤物写新目标/命令
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| Entity Tag System | ✅ 已有 |
| Blackboard System | ✅ 已有 |
| SelectionRuleRegistry | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| SelectionRule OwnedSummon filter | P2 | 需要自定义 filter 检查 "summoned" tag + owner ref |
| 召唤物 owner 记录机制 | P2 | 在召唤时写入 Blackboard owner entity ref |
