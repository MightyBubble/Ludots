# C3: 任意单位(效果不同)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 2 }  // ability slot
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
  preset: None (custom graph)
  Phase Graph:
    1. LoadContextTarget()
    2. GetRelationship(caster_team, target_team)
    3. CompareEqInt(relationship, Hostile)
    4. if hostile → ApplyEffectTemplate(polymorph)
    5. if friendly → ApplyEffectTemplate(haste_buff)
```

- 已有 `GetRelationship()` 在 `IGraphRuntimeApi`

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| GetRelationship() | ✅ 已有 |
| Phase Graph 条件分支 | ✅ 已有 |
| ApplyEffectTemplate Graph op | ✅ 已有 |

## 新增需求

无 — 所有依赖组件已实现。
