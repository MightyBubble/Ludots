# Mechanism: M7 — Tracking Sustained (持续追踪: 锁定目标持续生效)

> **Examples**: OW Mercy治疗光束, Dota Life Drain, OW Moira治疗喷雾

## 交互层

- **InputConfig**: ReactsTo = **Down** (或 DownAndUp)
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
```

## 实现方案

**锁定目标持续效果**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → create_tracking_beam
    → EffectClip: tracking_beam_effect
      period: 3 ticks
      duration: 300 ticks (或 until_up)
      target: order_target (locked)

  OnPeriod Phase Graph:
    1. ReadBlackboard(locked_target)
    2. if target IsDead():
       → DestroyEffect(self)  // 目标死亡断开
    3. else:
       → ApplyEffect(heal_tick, target=locked_target)  // 持续治疗
```

**关键差异** (与 M6 对比):
- **无距离断裂**: 即使目标移动到很远也不断开
- **目标锁定**: 初次选定后不再改变
- **死亡断开**: 目标死亡时自动断开

**视觉表现**:
```
Performer: TrackingBeam(origin=caster, target=locked_target)
  color: yellow (治疗) / purple (伤害)
  lifetime: 与 EffectClip 同步
  auto_follow: true (自动跟随目标移动)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| EffectClip (持续效果) | ✅ 已有 |
| Target locking | ✅ 已有 |
| IsDead check | ✅ 已有 |
| DestroyEffect | ✅ 已有 |
| TrackingBeam Performer | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| TrackingBeam Performer | P2 | 追踪光束渲染 (表现层) |
| Target death detection | P1 | 目标死亡时断开 |
