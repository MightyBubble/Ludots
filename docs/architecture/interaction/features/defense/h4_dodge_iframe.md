# H4: Dodge with Invincibility Frames (Iframe)

## Overview

A directional evasion maneuver (roll, dash, sidestep) that grants the player temporary invulnerability during a portion of the animation. The player can move through attacks unharmed during the iframe window, but is vulnerable before and after. Reference: DS roll, GoW dodge, Spider-Man dodge.

## User Experience

- Player presses the dodge button (e.g., Circle) + directional input
- Character performs a roll or dash animation in the chosen direction
- During the middle portion of the animation (e.g., ticks 4–12 of a 20-tick animation), the player is invulnerable to all damage
- Before and after the iframe window, the player can still be hit
- Dodge has a cooldown or stamina cost to prevent spam

## Implementation

The dodge ability applies an `invulnerable` tag for a specific duration window within the animation:

```
dodge_roll:
  inputBinding: Circle + Direction
  onActivate: PlayAnimation("roll", duration=20 ticks)
              + AddTag("dodging", duration=20 ticks)
              + Delay(4 ticks) → AddTag("invulnerable", duration=8 ticks)
              + ConsumeStamina(30)

on_incoming_damage:
  precondition: HasTag("invulnerable")
  effect: DamageMultiplier(0.0)
```

**Directional movement**: The dodge ability reads the directional input vector and applies a velocity impulse in that direction. If no direction is held, a default backward dodge is used.

**Stamina gating**: Each dodge consumes stamina. If stamina is insufficient, the ability is blocked. Stamina regenerates over time when not dodging.

**Animation lock**: The `dodging` tag prevents other abilities from activating until the animation completes, ensuring the player commits to the full dodge duration.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | `invulnerable` tag expires automatically after iframe window |
| IncomingDamage modifier hook | ✅ Existing | Negate damage when `invulnerable` tag is present |
| Delay effect | ⚠️ **Required** | Defer `invulnerable` tag application until iframe window starts |
| Directional input vector | ✅ Existing | Read movement stick to determine dodge direction |
| Stamina resource | ⚠️ **Required** | Consume stamina on dodge, block if insufficient |
| Animation lock tag | ✅ Existing | Prevent ability overlap during dodge animation |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/dodge_effects.json) ===
[
  {
    // 闪避动画锁 Buff：阻止其他能力激活
    "id": "Effect.Dodge.AnimLock",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 20 },
    "grantedTags": [
      { "tag": "Status.Dodging", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 无敌帧 Buff：在闪避动画中段生效
    "id": "Effect.Dodge.IframeBuff",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 8 },
    "grantedTags": [
      { "tag": "Status.Invulnerable", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 闪避位移（Displacement preset）
    "id": "Effect.Dodge.Displacement",
    "presetType": "Displacement",
    "lifetime": "Instant",
    "configParams": {
      "distanceCm": { "type": "float", "value": 600.0 },
      "durationTicks": { "type": "int", "value": 8 }
    }
  }
]

// === AbilityExecSpec: 闪避翻滚 ===
// 使用 timeline items 在不同 tick 偏移触发效果，替代虚构 Delay 机制
{
  "id": "Ability.Dodge.Roll",
  "blockTags": ["Status.Dodging"],           // 闪避期间不可再闪避
  "exec": {
    "totalTicks": 20,
    "items": [
      // tick 0: 施加闪避动画锁 Tag（持续 20 ticks）
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.AnimLock" },
      // tick 0: 向输入方向位移
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Dodge.Displacement" },
      // tick 4: 施加无敌帧 Buff（持续 8 ticks → tick 4~12 为无敌窗口）
      { "kind": "EffectSignal", "tick": 4, "effectId": "Effect.Dodge.IframeBuff" }
    ]
  }
}

// === ResponseChainListener：无敌帧伤害拦截 ===
// 挂载在拥有闪避能力的 Entity 上
{
  "response_chain_listeners": [
    {
      "eventTagId": "incoming_damage",
      "responseType": "Hook",
      "priority": 300,
      "responseGraphId": "Graph.Dodge.IframeCheck"
      // Graph.Dodge.IframeCheck:
      //   HasTag          E[0], Status.Invulnerable → B[0]
      //   JumpIfFalse     B[0], END    // 无无敌帧 → 不拦截
      //   (Hook 生效 → 取消伤害)
    }
  ]
}

// === 体力消耗 ===
// 闪避能力的体力消耗通过 AbilityExecSpec 的 cost 字段配置：
//   "cost": { "attribute": "Stamina", "amount": 30 }
// 体力不足时能力激活失败。
// 体力回复通过 Attribute Regen 系统配置（已有基建）。
```
