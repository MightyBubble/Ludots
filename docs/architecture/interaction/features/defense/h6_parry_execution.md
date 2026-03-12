# H6: Parry → Execution Window (Posture Break Finisher)

## Overview

A two-stage defensive system where successful parries accumulate posture damage on the enemy. When the enemy's posture bar is fully broken, a special execution/finisher prompt appears, allowing the player to perform a high-damage or instant-kill attack. Reference: Sekiro parry → posture break → deathblow, DS parry → riposte.

## User Experience

- Player successfully parries an enemy attack (see H2: Precision Parry)
- Each successful parry adds posture damage to the enemy's posture bar (visible UI gauge)
- When the enemy's posture bar fills completely, they enter a "posture broken" state (stagger animation, vulnerable)
- A prompt appears (e.g., "Press R1 for Deathblow")
- Player presses the execution button within the window to trigger a cinematic finisher animation with massive damage or instant kill
- If the window expires without input, the enemy recovers from the broken state

## Implementation

Each successful parry applies a `posture_damage` effect to the enemy. When cumulative posture exceeds the enemy's `PostureMax`, a `posture_broken` tag is applied and a `finisher_opportunity` tag is granted to the player:

```
parry_success:
  onParryHit: ApplyEffect(target=attacker, "posture_damage", amount=30)

enemy_posture_system:
  onPostureExceedsMax:
    AddTag(target=self, "posture_broken", duration=90 ticks)
    + PlayAnimation("stagger_heavy")
    + FireEvent("PostureBroken", target=self)

player_finisher_listener:
  trigger: OnEvent("PostureBroken")
  effect: AddTag("finisher_opportunity:{source_id}", duration=90 ticks)
          + SpawnVFX("finisher_prompt", attachTo=source)

player_finisher_ability:
  inputBinding: R1 (press)
  precondition: HasTag("finisher_opportunity:*")
  onActivate:   RemoveTag("finisher_opportunity:{matched_id}")
                + PlayAnimation("deathblow")
                + Damage(target=matched_source, amount=9999, ignoreArmor=true)
                + FireEvent("finisher_executed")
```

**Posture decay**: Enemy posture naturally decays over time when not being attacked. The decay rate is slower when the enemy is in an active attack animation.

**Posture bar UI**: A separate UI system listens for `posture_damage` events and updates a visual gauge above the enemy's head.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| PostureComponent (current, max, decay rate) | ⚠️ **Required** | Track cumulative posture damage per enemy |
| ApplyEffect("posture_damage") | ⚠️ **Required** | Increment posture on successful parry |
| FireEvent("PostureBroken") | ✅ Existing | Broadcast when posture threshold is exceeded |
| Tag duration (auto-expire) | ✅ Existing | Finisher window closes automatically after N ticks |
| Wildcard tag precondition (`finisher_opportunity:*`) | ⚠️ **Required** | Match any pending finisher regardless of source ID |
| Attached VFX lifetime binding | ⚠️ **Required** | Finisher prompt VFX despawns when tag expires |

## Configuration Example

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/parry_execution_effects.json) ===
[
  {
    // 弹反成功时施加给敌人的架势伤害（瞬时 Attribute 修改）
    "id": "Effect.ParryExec.PostureDamage",
    "presetType": "InstantDamage",
    "lifetime": "Instant",
    "configParams": {
      "PostureDamageAmount": { "type": "float", "value": 30.0 }
    },
    "phaseListeners": [
      {
        "phase": "OnApply",
        "graphProgramId": "Graph.ParryExec.ApplyPostureDamage"
        // Graph.ParryExec.ApplyPostureDamage:
        //   LoadContextTarget        E[1]
        //   LoadConfigFloat          "PostureDamageAmount" → F[0]
        //   ModifyAttributeAdd       E[effect], E[1], Posture, F[0]
      }
    ]
  },
  {
    // 架势击破后施加给敌人的击破状态
    "id": "Effect.ParryExec.PostureBroken",
    "presetType": "Buff",
    "lifetime": "After",
    "duration": { "durationTicks": 90 },
    "grantedTags": [
      { "tag": "Status.PostureBroken", "formula": "Fixed", "amount": 1 }
    ]
  },
  {
    // 处决伤害（瞬时，忽略护甲）
    "id": "Effect.ParryExec.DeathblowDamage",
    "presetType": "InstantDamage",
    "lifetime": "Instant",
    "configParams": {
      "DamageAmount": { "type": "float", "value": 9999.0 },
      "IsTrueDamage": { "type": "int", "value": 1 }
    }
  }
]

// === Attribute 定义 ===
// 在 AttributeSet 中注册 Posture 属性（P1 需求）：
//   { "id": "Posture", "defaultValue": 0, "min": 0, "max": 150 }
//   { "id": "PostureMax", "defaultValue": 150 }
//   { "id": "PostureDecayPerTick", "defaultValue": 0.5 }

// === Attribute-to-Tag Bridge（同 G8 combo_meter 模式）===
// Periodic Buff 每 tick 检查 Posture >= PostureMax，满足时授予 Status.PostureBroken
{
  "id": "Effect.ParryExec.PostureWatcher",
  "presetType": "Buff",
  "lifetime": "UntilRemoved",
  "configParams": {
    "periodTicks": { "type": "int", "value": 1 }
  },
  "phaseListeners": [
    {
      "phase": "OnPeriod",
      "graphProgramId": "Graph.ParryExec.PostureThresholdCheck"
    }
  ]
}

// === Graph Program: Graph.ParryExec.PostureThresholdCheck ===
//   LoadContextSource       E[0]             // 敌人自身
//   LoadAttribute           E[0], Posture    → F[0]
//   LoadAttribute           E[0], PostureMax → F[1]
//   CompareGtFloat          F[0], F[1]       → B[0]  // Posture > PostureMax?
//   JumpIfFalse             B[0], END
//   ApplyEffectTemplate     E[0], "Effect.ParryExec.PostureBroken"
//   SendEvent               "PostureBroken"

// === 玩家处决 AbilityExecSpec ===
{
  "id": "Ability.Deathblow",
  "activationRequireTags": ["Status.PostureBroken"],  // P1: 需 AbilityActivationRequireTags
  // 需要目标持有 Status.PostureBroken + 距离 < 100cm（ContextGroup 评分）
  "exec": {
    "totalTicks": 40,
    "items": [
      // tick 0: 施加处决伤害
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.ParryExec.DeathblowDamage" },
      // tick 0: 发送处决事件（触发动画/VFX）
      { "kind": "EventSignal", "tick": 0, "eventId": "finisher_executed" }
    ]
  }
}

// === 弹反成功时的连锁 ===
// H2 弹反 ResponseChainListener 中增加一条 Chain：
//   eventTagId: "incoming_damage"
//   responseType: Chain
//   effectTemplateId: "Effect.ParryExec.PostureDamage"  // 对攻击者累积架势
//   responseGraphId: "Graph.Parry.CheckWindow"          // 同 H2 前置条件

// === 架势衰减 ===
// Posture 属性自然衰减通过 Attribute Regen 系统配置：
//   { "attribute": "Posture", "regenPerTick": -0.5, "regenDelayTicks": 60 }
// 敌人处于攻击动画时，regenDelayTicks 重置（衰减暂停）。
```
