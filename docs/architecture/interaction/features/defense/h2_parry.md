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

```json
{
  "abilities": [
    {
      "id": "parry_press",
      "inputBinding": "L1",
      "inputMode": "Press",
      "onActivate": {
        "effects": [
          { "type": "AddTag", "tag": "parrying", "duration": 12 },
          { "type": "AddTag", "tag": "parry_window_open", "duration": 8 }
        ]
      }
    }
  ],
  "passives": [
    {
      "id": "parry_intercept",
      "trigger": "OnIncomingDamage",
      "precondition": { "all": ["parry_window_open"] },
      "effects": [
        { "type": "DamageMultiplier", "value": 0.0 },
        { "type": "RemoveTag", "tag": "parry_window_open" },
        { "type": "ApplyTagToAttacker", "tag": "staggered", "duration": 30 },
        { "type": "AddTag", "tag": "parry_recovery", "duration": 20 },
        { "type": "FireEvent", "event": "parry_success" }
      ]
    }
  ]
}
```
