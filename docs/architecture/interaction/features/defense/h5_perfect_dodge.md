# H5: Perfect Dodge (Bonus Reward on Precise Timing)

## Overview

An enhanced version of the standard dodge where the player receives a special reward if they dodge at the exact moment an attack would have landed. The timing window is tighter than a normal dodge iframe, and success grants a bonus effect such as slow-motion, damage buff, or instant counter opportunity. Reference: GoW Realm Shift (slow-motion), Spider-Man Perfect Dodge (instant counter).

## User Experience

- Player presses dodge button + direction at the last possible moment before an attack hits
- If timing is perfect (within a narrow window, e.g., 4–6 ticks before impact): a special visual/audio cue fires (slow-motion effect, golden flash)
- The player gains a temporary buff: time dilation (enemies move slower), damage boost, or an instant counter-attack window
- If timing is early or late: a normal dodge with standard iframes occurs, no bonus
- The perfect dodge window is significantly tighter than the standard iframe window

## Implementation

The perfect dodge ability checks for an incoming attack within a narrow time window at the moment of dodge activation. If an attack is detected, the `perfect_dodge` tag is applied instead of the standard `dodging` tag:

```
dodge_roll:
  inputBinding: Circle + Direction
  onActivate:
    IF (IncomingAttackWithinTicks(6)):
      PlayAnimation("perfect_dodge", duration=20 ticks)
      + AddTag("perfect_dodge", duration=20 ticks)
      + AddTag("invulnerable", duration=12 ticks)
      + ApplyEffect("time_dilation", duration=60 ticks, magnitude=0.5)
      + AddTag("counter_window", duration=30 ticks)
      + FireEvent("perfect_dodge_success")
    ELSE:
      PlayAnimation("roll", duration=20 ticks)
      + AddTag("dodging", duration=20 ticks)
      + Delay(4 ticks) → AddTag("invulnerable", duration=8 ticks)
```

**Time dilation**: A global or local time-scale modifier is applied to enemies within a radius, slowing their animations and movement by 50% for 60 ticks.

**Counter window**: The `counter_window` tag enables a follow-up counter-attack ability that is normally unavailable. This ability can be activated by pressing the attack button during the window.

**Detection logic**: `IncomingAttackWithinTicks(N)` queries the combat system for any active enemy attack that will land within the next N ticks. This requires enemy attacks to broadcast their impact timing in advance.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| IncomingAttackWithinTicks query | ⚠️ **Required** | Detect imminent attacks to determine perfect dodge eligibility |
| Tag duration (auto-expire) | ✅ Existing | `perfect_dodge` and `counter_window` tags expire automatically |
| Time dilation effect | ⚠️ **Required** | Slow enemy animations/movement for visual reward |
| Conditional ability branching | ⚠️ **Required** | Execute different effect chains based on timing check |
| FireEvent("perfect_dodge_success") | ✅ Existing | Trigger VFX, audio, and UI feedback |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/perfect_dodge_effects.json) ===
[
  // ── 普通闪避 Effects（复用 H4） ──
  {
    "id": "Effect.Dodge.AnimLock",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 20 },
    "grantedTags": [
      { "tag": "Status.Dodging", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    "id": "Effect.Dodge.IframeBuff",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 8 },
    "grantedTags": [
      { "tag": "Status.Invulnerable", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    "id": "Effect.Dodge.Displacement",
    "presetType": "Displacement",
    "lifetime": "Instant",
    "configParams": {
      "distanceCm": { "type": "float", "value": 600.0 },
      "durationTicks": { "type": "int", "value": 8 }
    }
  },

  // ── 精准闪避专属 Effects ──
  {
    // 精准闪避：更长的无敌帧
    "id": "Effect.PerfectDodge.ExtendedIframe",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 12 },
    "grantedTags": [
      { "tag": "Status.Invulnerable", "formula": "Fixed", "amount": 1 },
      { "tag": "Status.PerfectDodge", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 精准闪避后的时间膨胀 Buff（减速敌人）
    "id": "Effect.PerfectDodge.TimeDilation",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 60 },
    "grantedTags": [
      { "tag": "Status.TimeDilation", "formula": "Fixed", "amount": 1 }
    ],
    "configParams": {
      "timeScale": { "type": "float", "value": 0.5 },
      "radius": { "type": "float", "value": 1000.0 }
    }
  },
  {
    // 精准闪避后的反击窗口
    "id": "Effect.PerfectDodge.CounterWindow",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 30 },
    "grantedTags": [
      { "tag": "Status.CounterWindow", "formula": "Fixed", "amount": 1 }
    ]
  }
]

// === 两个独立 Ability 替代 conditionalBranch ===
// 通过 Tag 前置条件区分：精准闪避要求 Status.DodgeWindowNearImpact

// ── Ability 1: 精准闪避（仅在即将受击时可激活）──
{
  "id": "Ability.PerfectDodge",
  "activationRequireTags": ["Status.DodgeWindowNearImpact"],  // P1: 需 AbilityActivationRequireTags
  // 优先级高于普通闪避，同键位绑定时优先匹配
  "priority": 100,
  "blockTags": ["Status.Dodging"],
  "exec": {
    "totalTicks": 20,
    "items": [
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.AnimLock" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.Displacement" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.PerfectDodge.ExtendedIframe" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.PerfectDodge.TimeDilation" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.PerfectDodge.CounterWindow" },
      { "kind": "EventSignal",  "tick": 0, "eventId": "perfect_dodge_success" }
    ]
  }
}

// ── Ability 2: 普通闪避（fallback，无特殊前置条件）──
{
  "id": "Ability.Dodge.Roll",
  "priority": 50,
  "blockTags": ["Status.Dodging"],
  "exec": {
    "totalTicks": 20,
    "items": [
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.AnimLock" },
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.Displacement" },
      // tick 4: 延迟施加无敌帧（与 H4 一致）
      { "kind": "EffectSignal", "tick": 4, "effectId": "Effect.Dodge.IframeBuff" }
    ]
  }
}

// === Status.DodgeWindowNearImpact 的来源 ===
// 敌人攻击在即将命中前 6 ticks 广播 incoming_attack_imminent 事件，
// 玩家的 ResponseChainListener 以 Chain 方式创建一个 6-tick 的 Buff
// 授予 Status.DodgeWindowNearImpact Tag。
// 该检测机制为 P1 需求（IncomingAttackWithinTicks query）。
```
