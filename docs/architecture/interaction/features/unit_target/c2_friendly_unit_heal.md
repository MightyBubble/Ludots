# C2: 友方单位 — 治疗/增益

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 1 }  // ability slot
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
EffectTemplate:
  preset: Heal
  targetFilter: Friendly
  modifiers: [{ attr: health, op: Add, value: +150 }]
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| SelectionRuleRegistry | ✅ 已有 |
| EffectTemplate.Heal | ✅ 已有 |
| TargetFilter.Friendly | ✅ 已有 |

## 新增需求

无 — 所有依赖组件已实现。
