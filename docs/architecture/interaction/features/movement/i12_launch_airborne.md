# Mechanism: I12 击飞 (Launch Airborne)

> 将目标弹射到空中并施加 airborne 状态

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit / Direction**
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
  Item[0]: EffectSignal @ tick 0 → launch_effect
    → EffectPresetType: Displacement
    → DisplacementDescriptor:
      directionMode: Fixed (upward / 向上)
      distanceCm: 200 (弹射高度)
      durationTicks: 20
    → GrantedTags on target: ["airborne"]
      duration: (与 displacement 同步, 20 ticks)
    → 可追加: AddTag("knocked_up")
```

- 使用已有的 `DisplacementRuntimeSystem`
- Fixed 方向设置为向上 (vertical direction)
- `airborne` tag 标记空中状态, 可被其他技能利用
- 空中状态期间目标无法行动

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Airborne tag + 空中物理 | P2 | 空中状态行为约束 |
| 垂直方向 Fixed 位移 | P2 | upward direction vector |
| Tag 与 Displacement 时长同步 | P2 | airborne 持续到落地 |
