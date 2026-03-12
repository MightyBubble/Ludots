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

```json
{
  "parryAbility": {
    "id": "parry_press",
    "onParrySuccess": {
      "effects": [
        { "type": "ApplyEffect", "target": "attacker", "effect": "posture_damage", "amount": 30 }
      ]
    }
  },
  "enemyPosture": {
    "max": 150,
    "decayPerTick": 0.5,
    "decayDelayTicks": 60,
    "onPostureBreak": {
      "effects": [
        { "type": "AddTag", "tag": "posture_broken", "duration": 90 },
        { "type": "PlayAnimation", "id": "stagger_heavy" },
        { "type": "FireEvent", "event": "PostureBroken", "source": "self" }
      ]
    }
  },
  "playerPassive": {
    "id": "finisher_opportunity_listener",
    "trigger": "OnEvent:PostureBroken",
    "effects": [
      { "type": "AddTag", "tag": "finisher_opportunity:{sourceId}", "duration": 90 },
      { "type": "SpawnVFX", "vfx": "finisher_prompt", "attachTo": "{sourceId}" }
    ]
  },
  "playerAbility": {
    "id": "deathblow",
    "inputBinding": "R1",
    "inputMode": "Press",
    "precondition": { "anyTagPrefix": "finisher_opportunity" },
    "onActivate": {
      "effects": [
        { "type": "RemoveMatchedTag", "prefix": "finisher_opportunity" },
        { "type": "PlayAnimation", "id": "deathblow" },
        { "type": "Damage", "target": "matchedSource", "amount": 9999, "ignoreArmor": true },
        { "type": "FireEvent", "event": "finisher_executed" }
      ]
    }
  }
}
```
