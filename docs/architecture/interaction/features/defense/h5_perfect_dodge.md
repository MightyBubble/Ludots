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

```json
{
  "abilities": [
    {
      "id": "dodge_roll",
      "inputBinding": "Circle",
      "inputMode": "Press",
      "directionalInput": true,
      "cost": { "stamina": 30 },
      "onActivate": {
        "conditionalBranch": {
          "condition": { "type": "IncomingAttackWithinTicks", "ticks": 6 },
          "ifTrue": {
            "effects": [
              { "type": "PlayAnimation", "id": "perfect_dodge", "duration": 20 },
              { "type": "AddTag", "tag": "perfect_dodge", "duration": 20 },
              { "type": "AddTag", "tag": "invulnerable", "duration": 12 },
              { "type": "ApplyEffect", "effect": "time_dilation", "duration": 60, "magnitude": 0.5 },
              { "type": "AddTag", "tag": "counter_window", "duration": 30 },
              { "type": "FireEvent", "event": "perfect_dodge_success" }
            ]
          },
          "ifFalse": {
            "effects": [
              { "type": "PlayAnimation", "id": "roll", "duration": 20 },
              { "type": "AddTag", "tag": "dodging", "duration": 20 },
              { "type": "Delay", "ticks": 4, "then": [
                  { "type": "AddTag", "tag": "invulnerable", "duration": 8 }
                ]
              }
            ]
          }
        }
      }
    }
  ],
  "timeDilation": {
    "radius": 1000,
    "targetFilter": "enemies",
    "timeScale": 0.5
  }
}
```
