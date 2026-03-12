# H3: Counter Prompt (Reaction to Visual Cue)

## Overview

The game displays a visible prompt (exclamation mark, spider-sense flash, or similar) above an enemy when they begin a dangerous attack. The player must press the dedicated counter button within the prompt window to trigger a counter-attack animation. Missing the window results in taking full damage. Reference: Arkham Asylum counter (head exclamation mark), Spider-Man spider-sense.

## User Experience

- An enemy begins an attack; the game detects this and spawns a prompt indicator above them
- Player sees the exclamation mark (or similar cue) and presses the counter button (e.g., Triangle)
- If pressed within the window: a choreographed counter animation plays, the enemy attack is cancelled, and the enemy takes counter damage or enters a stun
- If the window expires without input: prompt disappears, the attack lands normally
- Multiple simultaneous prompts from different enemies are handled in priority order

## Implementation

The enemy ability system fires a `CounterWindowOpen` event when an attack enters its wind-up phase. The player's passive listener creates a timed `counter_opportunity` tag paired with the source entity ID:

```
enemy_attack_windup:
  onWindupStart: FireEvent("CounterWindowOpen", source=self, duration=24 ticks)

player_counter_listener:
  trigger: OnEvent("CounterWindowOpen")
  effect: AddTag("counter_opportunity:{source_id}", duration=24 ticks)
          + SpawnVFX("exclamation_mark", attachTo=source)

player_counter_ability:
  inputBinding: Triangle (press)
  precondition: HasTag("counter_opportunity:*")     # any pending counter
  onActivate:   RemoveTag("counter_opportunity:{matched_id}")
                + CancelAbility(target=matched_source)
                + PlayAnimation("counter_strike")
                + Damage(target=matched_source, amount=80)
                + ApplyTag(target=matched_source, "stunned", duration=20)
                + FireEvent("counter_success")
```

**Priority resolution**: When multiple `counter_opportunity` tags are active, the system selects the one with the earliest expiry (most urgent) to resolve first.

**VFX cleanup**: `SpawnVFX` is tied to the tag lifetime; when the tag expires or is consumed, the VFX is automatically destroyed.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| FireEvent from enemy abilities | ✅ Existing | Broadcast `CounterWindowOpen` from attacker wind-up |
| Tag duration (auto-expire) | ✅ Existing | Counter window closes automatically if not used |
| CancelAbility on target | ⚠️ **Required** | Stop the enemy's attack mid-animation on successful counter |
| Wildcard tag precondition (`counter_opportunity:*`) | ⚠️ **Required** | Match any pending counter opportunity regardless of source ID |
| Attached VFX lifetime binding | ⚠️ **Required** | Exclamation mark VFX must despawn when tag expires |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/counter_prompt_effects.json) ===
[
  {
    // 敌人风筝阶段施加给玩家的反击机会 Buff
    // Tag 过期时 VFX 也随之销毁（VFX 绑定 Tag 生命周期）
    "id": "Effect.CounterPrompt.Opportunity",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 24 },
    "grantedTags": [
      { "tag": "Status.CounterOpportunity", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 反击成功后施加给敌人的眩晕
    "id": "Effect.CounterPrompt.EnemyStun",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 20 },
    "grantedTags": [
      { "tag": "Status.Stunned", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 反击伤害（瞬时）
    "id": "Effect.CounterPrompt.StrikeDamage",
    "presetType": "InstantDamage",
    "lifetime": "Instant",
    "configParams": {
      "DamageCoeff": { "type": "float", "value": 80.0 }
    }
  }
]

// === 敌人 AbilityExecSpec：攻击风筝阶段广播事件 ===
{
  "id": "Ability.Enemy.MeleeAttack",
  "exec": {
    "totalTicks": 40,
    "items": [
      // tick 0 (wind-up 开始): 发送 CounterWindowOpen 事件
      { "kind": "EventSignal", "tick": 0, "eventId": "CounterWindowOpen" }
    ]
  }
}

// === 玩家 ResponseChainListener：监听 CounterWindowOpen ===
// 当收到事件时，Chain 创建 CounterOpportunity Buff（授予 Tag + VFX）
{
  "response_chain_listeners": [
    {
      "eventTagId": "CounterWindowOpen",
      "responseType": "Chain",
      "priority": 100,
      "effectTemplateId": "Effect.CounterPrompt.Opportunity"
    }
  ]
}

// === 玩家反击 AbilityExecSpec ===
{
  "id": "Ability.Counter.Strike",
  "activationRequireTags": ["Status.CounterOpportunity"],  // P1: 需要 AbilityActivationRequireTags
  "exec": {
    "totalTicks": 25,
    "items": [
      // tick 0: 对匹配目标施加伤害
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.CounterPrompt.StrikeDamage" },
      // tick 0: 对匹配目标施加眩晕
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.CounterPrompt.EnemyStun" },
      // tick 0: 发送 counter_success 事件（触发 VFX/音效）
      { "kind": "EventSignal", "tick": 0, "eventId": "counter_success" }
    ]
  }
}

// 注: 多个同时存在的 CounterOpportunity 由 ContextGroup 评分机制（P1）
// 选择最紧急（过期最早）的目标。
// Wildcard tag precondition（P1）或 ContextGroup 评分替代 "counter_opportunity:*" 匹配。
```
