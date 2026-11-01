# Mechanism: I12 击飞 (Launch Airborne)

> 将目标弹射到空中并施加 airborne 状态

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit / Direction**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
