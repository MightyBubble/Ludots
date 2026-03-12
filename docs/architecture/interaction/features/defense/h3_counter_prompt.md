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

```json
{
  "enemyAbility": {
    "id": "enemy_melee_attack",
    "onWindupStart": {
      "effects": [
        {
          "type": "FireEvent",
          "event": "CounterWindowOpen",
          "params": { "sourceId": "self", "windowTicks": 24 }
        }
      ]
    }
  },
  "playerPassive": {
    "id": "counter_opportunity_listener",
    "trigger": "OnEvent:CounterWindowOpen",
    "effects": [
      { "type": "AddTag", "tag": "counter_opportunity:{sourceId}", "duration": 24 },
      { "type": "SpawnVFX", "vfx": "exclamation_mark", "attachTo": "{sourceId}" }
    ]
  },
  "playerAbility": {
    "id": "counter_strike",
    "inputBinding": "Triangle",
    "inputMode": "Press",
    "precondition": { "anyTagPrefix": "counter_opportunity" },
    "onActivate": {
      "effects": [
        { "type": "RemoveMatchedTag", "prefix": "counter_opportunity" },
        { "type": "CancelAbility", "target": "matchedSource" },
        { "type": "PlayAnimation", "id": "counter_strike" },
        { "type": "Damage", "target": "matchedSource", "amount": 80 },
        { "type": "ApplyTag", "target": "matchedSource", "tag": "stunned", "duration": 20 }
      ]
    }
  }
}
```
