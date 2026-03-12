# C8: 传送到目标身边

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillB"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 7 }  // ability slot
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
AbilityExecSpec:
  Item[0]: EffectSignal → teleport_to_target
    → Phase Graph:
      1. LoadContextTarget() → E[1]
      2. 读取 E[1].Position
      3. 设置 E[0].Position = E[1].Position + offset
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| LoadContextTarget Graph op | ✅ 已有 |
| Position 读写 Graph ops | ✅ 已有 |
| Phase Graph System | ✅ 已有 |

## 新增需求

无 — 所有依赖组件已实现。
