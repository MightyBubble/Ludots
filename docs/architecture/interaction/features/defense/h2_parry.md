# H2: Precision Parry (Tight Timing Window)

## Overview

A high-skill defensive technique where the player must press the block/parry button at the exact moment an attack lands. The timing window is narrow (typically 4–10 ticks). A successful parry fully negates the incoming damage and triggers a follow-up reward state. Reference: DS/Sekiro L1 at precise timing.

## User Experience

- An enemy begins an attack animation (telegraphed wind-up)
- Player presses the parry button at the moment of impact
- If timing is correct: a distinct audio/visual cue fires (metal clash, sparks), damage is negated, and the attacker enters a stagger or posture-damage state
- If timing is early or late: the press is treated as a normal block (or whiffs entirely) with no special reward
- No sustained hold required — single press with frame-precise window

## Implementation

The parry ability activates a short `parrying` tag window. The incoming damage handler checks for both the `parrying` tag and the `parry_active_frame` window before full negation:

```
parry_press:
  inputBinding: L1 (press)
  onActivate: AddTag("parrying", duration=8 ticks)
                + AddTag("parry_window_open", duration=8 ticks)

on_incoming_damage:
  precondition: HasTag("parrying") AND HasTag("parry_window_open")
  effect: DamageMultiplier(0.0)
          + RemoveTag("parry_window_open")
          + ApplyEffect(target=attacker, "staggered", duration=30 ticks)
          + FireEvent("parry_success")
```

**Window granularity**: `parry_window_open` tag duration controls how many ticks the parry is active. A separate `parry_recovery` tag (applied after success/expiry) enforces a cooldown before the next parry attempt.

**Failure handling**: If `parry_window_open` expires without intercepting a hit, no stagger is granted. The `parrying` tag may linger slightly longer (e.g., 4 extra ticks) to provide minimal guard-like coverage.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Parry window closes automatically after N ticks |
| IncomingDamage modifier hook | ✅ Existing | Intercept hit, check `parry_window_open`, negate damage |
| ApplyEffect on attacker | ✅ Existing | Grant stagger/posture damage to attacker on success |
| FireEvent("parry_success") | ✅ Existing | Trigger VFX, audio, and downstream systems |
| Parry cooldown tag | ⚠️ **Required** | Prevent spam; enforce minimum gap between parry attempts |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/parry_effects.json) ===
[
  {
    // 弹反窗口 Buff：按下弹反键后施加，授予 parry_window_open Tag
    "id": "Effect.Parry.WindowBuff",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 8 },
    "grantedTags": [
      { "tag": "Status.Parrying", "formula": "Fixed", "amount": 1 },
      { "tag": "Status.ParryWindowOpen", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 弹反成功后冷却：防止连续弹反
    "id": "Effect.Parry.RecoveryCooldown",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 20 },
    "grantedTags": [
      { "tag": "Status.ParryRecovery", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 施加给攻击者的硬直 Buff
    "id": "Effect.Parry.AttackerStagger",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 30 },
    "grantedTags": [
      { "tag": "Status.Staggered", "formula": "Fixed", "amount": 1 }
    ]
  }
]

// === AbilityExecSpec (mods/<yourMod>/GAS/abilities.json) ===
{
  "id": "Ability.Parry.Press",
  "blockTags": ["Status.ParryRecovery"],       // 冷却中不可再弹反
  "exec": {
    "totalTicks": 12,
    "items": [
      // tick 0: 施加弹反窗口 Buff（自动在 8 tick 后过期，移除 Tag）
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Parry.WindowBuff" }
    ]
  }
}

// === ResponseChainListener（挂载在持有弹反能力的 Entity 上）===
// 监听 incoming_damage 事件，当持有 Status.ParryWindowOpen 时：
//   1. Hook（取消伤害）
//   2. Chain（对攻击者施加硬直 + 对自身施加冷却）
{
  "response_chain_listeners": [
    {
      // Hook: 取消伤害
      "eventTagId": "incoming_damage",
      "responseType": "Hook",
      "priority": 200,
      "responseGraphId": "Graph.Parry.CheckWindow"
      // Graph.Parry.CheckWindow:
      //   HasTag E[0], Status.ParryWindowOpen → B[0]
      //   JumpIfFalse B[0], SKIP   // 无弹反窗口则不触发
      //   (Hook 生效 → 取消伤害)
    },
    {
      // Chain: 对攻击者施加硬直
      "eventTagId": "incoming_damage",
      "responseType": "Chain",
      "priority": 201,
      "effectTemplateId": "Effect.Parry.AttackerStagger",
      "responseGraphId": "Graph.Parry.CheckWindow"
    },
    {
      // Chain: 对自身施加弹反冷却
      "eventTagId": "incoming_damage",
      "responseType": "Chain",
      "priority": 202,
      "effectTemplateId": "Effect.Parry.RecoveryCooldown",
      "responseGraphId": "Graph.Parry.CheckWindow"
    }
  ]
}

// === Graph Program: Graph.Parry.CheckWindow ===
// 前置条件检查：仅在持有 Status.ParryWindowOpen 时允许响应
//   E[0] = responder (弹反者)
//   HasTag          E[0], Status.ParryWindowOpen → B[0]
//   JumpIfFalse     B[0], END                    // 无窗口 → 不响应
//   SendEvent       "parry_success"              // 触发 VFX/音效
```
