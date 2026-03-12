# Mechanism: M6 — Tether Distance Break (牵引/连接: 距离过远断裂)

> **Examples**: Dota Io Tether, Razor Link, LoL Karma W, LoL Morgana R

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
```

## 实现方案

**建立牵引连接**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → create_tether
    → EffectClip: tether_effect
      period: 5 ticks
      duration: 300 ticks
      target: order_target

  OnPeriod Phase Graph:
    1. distance = CalcDistance(caster, target)
    2. if distance > break_range (800cm):
       → DestroyEffect(self)  // 断裂
    3. else:
       → ApplyEffect(tether_tick_effect, target)  // 持续效果
```

**牵引效果** (可选):
```
tether_tick_effect:
  → ModifyAttribute(target, move_speed, multiply=0.7)  // 减速
  → ApplyDamage(target, 10)  // 持续伤害
```

**视觉表现**:
```
Performer: TetherLine(origin=caster, target=target)
  color: purple
  lifetime: 与 EffectClip 同步
  break_visual: 距离过远时显示断裂特效
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| EffectClip (持续效果) | ✅ 已有 |
| CalcDistance Graph op | ✅ 已有 |
| Conditional break logic | ✅ 已有 |
| DestroyEffect | ✅ 已有 |
| TetherLine Performer | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| TetherLine Performer | P2 | 连接线渲染 (表现层) |
| Distance-based break | P1 | 距离检测并销毁效果 |
