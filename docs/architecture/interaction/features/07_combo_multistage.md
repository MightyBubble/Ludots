# Feature: Combo / Multi-Stage Abilities (G1–G11)

> 清单覆盖: G1 轻攻击连段, G2 轻重混合, G3 方向+攻击, G4 二段重激活, G5 三段, G6 时间窗口, G7 命中确认, G8 连击计数解锁, G9 翻滚后攻击, G10 格挡后攻击, G11 引导中分段射击

## 交互层

- **InputConfig**: ReactsTo = **Down** (每段都是独立 Press)
- **TargetMode**: varies (None / Unit / Direction)
- **Acquisition**: Explicit 或 ContextScored

## 核心洞察

连击不是交互层的特殊模式。每一段连击都是**独立的 Press**, 段与段之间的关联完全由 **Tag 状态机 + Attribute** 驱动:

```
Hit1: Press → Execute → 加 combo_stage:1 Tag (duration=窗口期)
Hit2: Press → Precondition: HasTag(combo_stage:1) → Execute → 加 combo_stage:2
Hit3: Press → Precondition: HasTag(combo_stage:2) → Execute → 移除所有 combo tags
超时: combo_stage:1 Tag 到期自动移除 → 重置
```

## 实现方案

### G1: 轻攻击连段(动画递进)

```
需要 3 个 AbilityDefinition, 绑定同一个 InputBinding:
  light_attack_1:
    precondition: NOT HasTag("combo_stage")
    exec: damage + AddTag("combo_stage:1", duration=30 ticks)
    performer: swing_animation_1

  light_attack_2:
    precondition: HasTag("combo_stage:1")
    exec: damage + RemoveTag("combo_stage:1") + AddTag("combo_stage:2", duration=25 ticks)
    performer: swing_animation_2

  light_attack_3:
    precondition: HasTag("combo_stage:2")
    exec: heavy_damage + RemoveTag("combo_stage:2")
    performer: swing_animation_3

路由: 同一 InputBinding → OrderSubmitter 检查 precondition → 激活匹配的 ability
```

- **已有**: Ability 级别的 Tag precondition 检查 (`AbilityActivationBlockTags.RequiredAll` 已支持正向 Tag 门控)

### G2: 轻重混合连段

```
同 G1, 但路由依据是 不同的 InputBinding + combo_stage tag:
  R1 (light) → combo_stage:1
  R1 (light) → combo_stage:2
  R2 (heavy) with combo_stage:1 → heavy_finisher_A
  R2 (heavy) with combo_stage:2 → heavy_finisher_B
```

### G3: 方向+攻击=不同招

```
实现:
  InputOrderMapping 的 ArgsTemplate 传入 InputDirection:
    forward+R2 → argsTemplate: { i0: slot_jump_attack }
    back+R1   → argsTemplate: { i0: slot_backstep_attack }

  或: ContextScored 路由 — ContextGroup 根据 input direction 评分:
    score_factor: input_direction_dot_forward > 0.7 → jump_attack
    score_factor: input_direction_dot_forward < -0.7 → backstep_attack
```

### G4/G5: 技能二段/三段重激活

```
LoL Lee Sin Q:
  Q press 1: LaunchProjectile(skillshot)
    → OnHit: AddTag("q1_hit", target=hit_entity, duration=60 ticks) on caster
    → AddTag("q1_available") on caster

  Q press 2: Precondition: HasTag("q1_available")
    → Dash to entity stored in Blackboard (from q1_hit)
    → RemoveTag("q1_available")
```

### G6: 时间窗口(太慢重置)

```
combo_stage:N Tag 的 duration = 窗口时间 (如 25-30 ticks)
到期自动移除 → 重置到 stage 0
已有: EffectLifetimeSystem 自动清理到期 tag
```

### G7: 必须命中才能接下段

```
Hit1:
  exec: 挥砍 (damage effect)
  OnHit callback (effect has ResponseChainListener):
    → Chain effect: AddTag("hit_confirmed", duration=20 ticks)

Hit2:
  precondition: HasTag("hit_confirmed")
  → 如果 Hit1 没命中, tag 不存在, Hit2 被拒绝 → combo 重置
```

### G8: 连击计数解锁

```
combo_meter Attribute:
  每次命中 → combo_meter += 1
  被命中 → combo_meter = 0

special_combo ability:
  precondition: combo_meter >= threshold
  exec: 特殊终结技
  onComplete: combo_meter = 0
```

### G9/G10: 翻滚/格挡后接攻击

```
翻滚/格挡 ability:
  onComplete → AddTag("post_dodge", duration=15 ticks)
  或: onComplete → AddTag("post_block", duration=15 ticks)

特殊攻击:
  precondition: HasTag("post_dodge") 或 HasTag("post_block")
```

### G11: 引导中分段射击

```
Jhin R:
  ability exec:
    Item[0]: TagClip "channeling", "cone_aiming"
    Item[1]: InputGate @ tick 0 (wait for fire input)
    Item[2]: EffectSignal → shot_1 (read current aim direction)
    Item[3]: InputGate (wait for next fire)
    Item[4]: EffectSignal → shot_2
    ...
    Item[8]: EffectSignal → shot_4 (final shot, extra damage)
```

- 已有: `InputGate` 在 AbilityExecSpec

## 依赖组件

| 组件 | 状态 |
|------|------|
| Tag duration (auto-expire) | ✅ 已有 |
| AbilityActivationBlockTags | ✅ 已有 |
| InputGate in AbilityExecSpec | ✅ 已有 |
| ResponseChainListener (OnHit) | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| ~~**AbilityActivationBlockTags.RequiredAll**~~ (正向 Tag 门控) | ~~**P0**~~ | ✅ 已有 — RequiredAll 字段已支持 (G1-G10) |
| Ability precondition 基于 Attribute 阈值 (combo_meter >= N) | P1 | G8 |
| ContextGroup 路由 (同一 binding → 多候选 ability) | P1 | G1, G3 (动作游戏) |
| Input direction dot product 评分 | P2 | G3 |
