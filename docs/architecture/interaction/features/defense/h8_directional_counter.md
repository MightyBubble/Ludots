# H8: Directional Counter (Mikiri Counter / Dash-Into-Attack)

## Overview

A specialized counter technique where the player must dodge or dash in a specific direction (typically toward the attacker) at the precise moment of an incoming attack. Success negates the attack and triggers a unique counter animation with bonus damage. Reference: Sekiro Mikiri Counter (forward dash into thrust attack).

## User Experience

- An enemy begins a specific attack type (e.g., thrust, sweep)
- Player presses dodge + forward direction at the exact moment the attack would land
- If timing and direction are correct: a special counter animation plays (e.g., stepping on the enemy's weapon), the attack is negated, and the enemy takes posture damage or stagger
- If direction is wrong (e.g., backward or sideways): a normal dodge occurs with no counter bonus
- If timing is wrong: the attack lands normally

## Implementation

The directional counter ability checks both the `deflecting` tag and the directional input vector at the moment of activation. If the input direction aligns with the attacker's position (within a tolerance angle), the counter is triggered:

```
directional_counter:
  inputBinding: Circle + Direction
  onActivate:
    IF (IncomingAttackWithinTicks(6) AND DirectionTowardAttacker(tolerance=30°)):
      PlayAnimation("mikiri_counter", duration=25 ticks)
      + AddTag("countering", duration=25 ticks)
      + AddTag("invulnerable", duration=15 ticks)
      + ApplyEffect(target=attacker, "posture_damage", amount=50)
      + ApplyTag(target=attacker, "staggered", duration=40)
      + FireEvent("directional_counter_success")
    ELSE:
      [normal dodge logic]

on_incoming_damage:
  precondition: HasTag("countering")
  effect: DamageMultiplier(0.0)
```

**Direction validation**: `DirectionTowardAttacker(tolerance)` compares the input vector with the vector from the player to the attacker. If the angle delta is within the tolerance (e.g., ±30°), the check passes.

**Attack type filtering** (optional): The counter can be restricted to specific attack types (e.g., only thrust attacks) by checking the incoming attack's `AttackType` tag.

**Posture damage**: Successful directional counters apply significant posture damage to the attacker, contributing to posture break (see H6).

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| IncomingAttackWithinTicks query | ⚠️ **Required** | Detect imminent attacks to determine counter eligibility |
| DirectionTowardAttacker check | ⚠️ **Required** | Validate that input direction points toward the attacker |
| Tag duration (auto-expire) | ✅ Existing | `countering` and `invulnerable` tags expire automatically |
| ApplyEffect("posture_damage") | ⚠️ **Required** | Increment attacker's posture on successful counter |
| Conditional ability branching | ⚠️ **Required** | Execute different effect chains based on direction check |
| AttackType tag filtering | Optional | Restrict counter to specific attack types (thrust, sweep, etc.) |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/directional_counter_effects.json) ===
[
  {
    // 定向反击动画锁 + 无敌
    "id": "Effect.DirCounter.AnimLock",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 25 },
    "grantedTags": [
      { "tag": "Status.Countering", "formula": "Fixed", "amount": 1 },
      { "tag": "Status.Invulnerable", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 对攻击者施加架势伤害
    "id": "Effect.DirCounter.PostureDamage",
    "presetType": "InstantDamage",
    "lifetime": "Instant",
    "configParams": {
      "PostureDamageAmount": { "type": "float", "value": 50.0 }
    },
    "phaseListeners": [
      {
        "phase": "OnApply",
        "graphProgramId": "Graph.DirCounter.ApplyPostureDamage"
        // Graph.DirCounter.ApplyPostureDamage:
        //   LoadContextTarget        E[1]
        //   LoadConfigFloat          "PostureDamageAmount" → F[0]
        //   ModifyAttributeAdd       E[effect], E[1], Posture, F[0]
      }
    ]
  },
  {
    // 对攻击者施加硬直
    "id": "Effect.DirCounter.AttackerStagger",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 40 },
    "grantedTags": [
      { "tag": "Status.Staggered", "formula": "Fixed", "amount": 1 }
    ]
  }
]

// === 两个独立 Ability 替代 conditionalBranch ===
// 通过 ContextGroup 评分（P1）验证方向 + 攻击类型

// ── Ability 1: 定向反击（Mikiri Counter）──
// ContextGroup 评分器验证：
//   1. 最近敌人 HasTag("Status.Thrusting") 或 HasTag("Status.Sweeping")
//   2. 输入方向与敌人方位角度差 < 30°
//   3. 距离 < 200cm
{
  "id": "Ability.DirCounter.Mikiri",
  "activationRequireTags": ["Status.DirCounterEligible"],  // P1: 需 AbilityActivationRequireTags
  "priority": 100,
  "blockTags": ["Status.Countering", "Status.Dodging"],
  "exec": {
    "totalTicks": 25,
    "items": [
      // tick 0: 施加反击动画锁 + 无敌
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.DirCounter.AnimLock" },
      // tick 0: 对目标施加架势伤害
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.DirCounter.PostureDamage" },
      // tick 0: 对目标施加硬直
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.DirCounter.AttackerStagger" },
      // tick 0: 发送成功事件
      { "kind": "EventSignal", "tick": 0, "eventId": "directional_counter_success" }
    ]
  }
}

// ── Ability 2: 普通闪避（fallback，复用 H4）──
{
  "id": "Ability.Dodge.Roll",
  "priority": 50,
  "blockTags": ["Status.Dodging"],
  "exec": {
    "totalTicks": 20,
    "items": [
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.AnimLock" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.Displacement" },
      { "kind": "EffectSignal", "tick": 4, "effectId": "Effect.Dodge.IframeBuff" }
    ]
  }
}

// === ResponseChainListener：反击期间伤害拦截 ===
{
  "response_chain_listeners": [
    {
      "eventTagId": "incoming_damage",
      "responseType": "Hook",
      "priority": 300,
      "responseGraphId": "Graph.DirCounter.InvulCheck"
      // Graph.DirCounter.InvulCheck:
      //   HasTag          E[0], Status.Invulnerable → B[0]
      //   JumpIfFalse     B[0], END
      //   (Hook 生效 → 取消伤害)
    }
  ]
}

// === 方向验证 ===
// 方向验证通过 ContextGroup 评分机制（P1）实现：
//   - 评分器读取输入方向向量和目标方位向量
//   - 计算角度差，超出 tolerance（30°）则评分为 0（不匹配）
//   - 匹配成功时授予 Status.DirCounterEligible Tag
//
// 攻击类型过滤（可选）：
//   ContextGroup 评分器额外检查目标是否持有
//   Status.Thrusting 或 Status.Sweeping Tag
```
