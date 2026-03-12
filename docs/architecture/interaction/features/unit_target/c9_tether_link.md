# C9: 牵引/连接

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillG"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 8 }  // ability slot
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
  1. EffectClip (duration=tether_duration)
  2. GrantedTags on caster: "tethered"
  3. GrantedTags on target: "tethered_target"
  4. Tether Effect 的 OnPeriod Graph:
     a. 计算 caster-target 距离
     b. if 距离 > break_range → RemoveTag, 销毁 effect
     c. else → ApplyEffectTemplate(heal/damage per tick)
```

- 已有: Periodic effect + Graph 计算距离

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| EffectClip duration | ✅ 已有 |
| GrantedTags | ✅ 已有 |
| Periodic effect | ✅ 已有 |
| 距离计算 Graph ops | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Tether 组件 (持续距离监测 + 断裂) | P1 | 需要专门的 Tether 系统或通过 Periodic effect + Graph 实现 |
