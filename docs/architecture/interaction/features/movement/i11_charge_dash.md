# Mechanism: I11 冲锋 (Charge Dash)

> 持续前冲, 撞到敌人时造成眩晕和伤害 (Reinhardt)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

```
Down → Direction:
  AbilityExecSpec:
    Item[0]: EffectSignal @ tick 0 → charge_effect
      1. AddTag("charging")
      2. EffectClip: displacement_continuous
         → directionMode: Fixed (from order direction)
         → speed: high (持续位移)
      3. PeriodicSearch (period=1 tick, front cone):
         → if hit enemy:
           a. FanOutApplyEffect(stun_effect, duration=60)
           b. FanOutApplyEffect(damage_effect)
           c. StopEffect(displacement_continuous)
           d. RemoveTag("charging")
      4. Duration: 180 ticks (最大冲锋时长)
      5. OnExpire: RemoveTag("charging")
```

- 冲锋碰撞检测需要 Displacement 停止机制
- 前置碰撞锥 (front cone) PeriodicSearch

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Displacement 碰撞停止 | P2 | 撞到目标后中止位移 |
| 前向碰撞锥检测 | P2 | PeriodicSearch front cone |
| StopEffect Graph op | P2 | 运行时中止指定 effect |
