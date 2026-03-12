# C4: 死亡友方 — 复活

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 3 }  // ability slot
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
需求: TargetFilter 需支持 "DeadAlly" 过滤
实现:
  1. 死亡单位保留 Entity (添加 "dead" tag, 不立即销毁)
  2. SelectionRule 新增: filter=DeadFriendly
  3. Ability Phase Graph:
     a. 检查 target HasTag("dead")
     b. RemoveTag("dead"), SetAttribute(health, base_health * 0.5)
```

- **需要**: `SelectionRuleRegistry` 新增 DeadFriendly filter, 或 SelectionRule.RelationshipFilter 扩展

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| Entity Tag System | ✅ 已有 |
| HasTag/RemoveTag Graph ops | ✅ 已有 |
| SetAttribute Graph op | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| SelectionRule 支持 DeadFriendly filter | P1 | 需要扩展 SelectionRuleRegistry 或 RelationshipFilter |
| 死亡单位保留机制 | P1 | 死亡时添加 "dead" tag 而非立即销毁 Entity |
