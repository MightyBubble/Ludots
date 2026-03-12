# Mechanism: I8 位置互换 (Position Swap)

> 与目标单位交换双方位置

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
  selectionType: Unit
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → swap_effect
    → Phase Graph:
      1. ReadPosition(caster) → temp_pos
      2. ReadPosition(target) → target_pos
      3. WritePosition(caster, target_pos)
      4. WritePosition(target, temp_pos)
```

- **不使用** DisplacementPreset (双向即时位置设置)
- 需要原子性操作: 两个写入必须同帧完成
- 不产生位移过程

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 即时位置设置 Graph op | P1 | ReadPosition / WritePosition |
| 原子双写支持 | P1 | 同帧完成双方位置互换 |
