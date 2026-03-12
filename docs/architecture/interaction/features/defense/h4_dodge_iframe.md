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
        "effects": [
          { "type": "PlayAnimation", "id": "roll", "duration": 20 },
          { "type": "AddTag", "tag": "dodging", "duration": 20 },
          { "type": "Delay", "ticks": 4, "then": [
              { "type": "AddTag", "tag": "invulnerable", "duration": 8 }
            ]
          },
          { "type": "ApplyVelocity", "direction": "inputVector", "magnitude": 600 }
        ]
      }
    }
  ],
  "passives": [
    {
      "id": "iframe_protection",
      "trigger": "OnIncomingDamage",
      "precondition": { "all": ["invulnerable"] },
      "effects": [
        { "type": "DamageMultiplier", "value": 0.0 }
      ]
    }
  ],
  "stamina": {
    "max": 100,
    "regenPerTick": 2
  }
}
```
