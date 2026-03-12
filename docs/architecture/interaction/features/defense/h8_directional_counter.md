# H8: Directional Counter (Mikiri Counter / Dash-Into-Attack)

## Overview

A specialized counter technique where the player must dodge or dash in a specific direction (typically toward the attacker) at the precise moment of an incoming attack. Success negates the attack and triggers a unique counter animation with bonus damage. Reference: Sekiro Mikiri Counter (forward dash into thrust attack).

## User Experience

- An enemy begins a specific attack type (e.g., thrust, sweep)
- Player presses dodge + forward direction at the exact moment the attack would land
- If timing and direction are correct: a special counter animation plays (e.g., stepping on the enemy's weapon), the attack is negated, and the enemy takes posture damage or stagger
- If direction is wrong (e.g., backward or sideways): a normal dodge occurs with no counter bonus
- If timing is wrong: the attack lands normally

## Implementation

The directional counter ability checks both the `deflecting` tag and the directional input vector at the moment of activation. If the input direction aligns with the attacker's position (within a tolerance angle), the counter is triggered:

```
directional_counter:
  inputBinding: Circle + Direction
  onActivate:
    IF (IncomingAttackWithinTicks(6) AND DirectionTowardAttacker(tolerance=30°)):
      PlayAnimation("mikiri_counter", duration=25 ticks)
      + AddTag("countering", duration=25 ticks)
      + AddTag("invulnerable", duration=15 ticks)
      + ApplyEffect(target=attacker, "posture_damage", amount=50)
      + ApplyTag(target=attacker, "staggered", duration=40)
      + FireEvent("directional_counter_success")
    ELSE:
      [normal dodge logic]

on_incoming_damage:
  precondition: HasTag("countering")
  effect: DamageMultiplier(0.0)
```

**Direction validation**: `DirectionTowardAttacker(tolerance)` compares the input vector with the vector from the player to the attacker. If the angle delta is within the tolerance (e.g., ±30°), the check passes.

**Attack type filtering** (optional): The counter can be restricted to specific attack types (e.g., only thrust attacks) by checking the incoming attack's `AttackType` tag.

**Posture damage**: Successful directional counters apply significant posture damage to the attacker, contributing to posture break (see H6).

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| IncomingAttackWithinTicks query | ⚠️ **Required** | Detect imminent attacks to determine counter eligibility |
| DirectionTowardAttacker check | ⚠️ **Required** | Validate that input direction points toward the attacker |
| Tag duration (auto-expire) | ✅ Existing | `countering` and `invulnerable` tags expire automatically |
| ApplyEffect("posture_damage") | ⚠️ **Required** | Increment attacker's posture on successful counter |
| Conditional ability branching | ⚠️ **Required** | Execute different effect chains based on direction check |
| AttackType tag filtering | Optional | Restrict counter to specific attack types (thrust, sweep, etc.) |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "directional_counter",
      "inputBinding": "Circle",
      "inputMode": "Press",
      "directionalInput": true,
      "onActivate": {
        "conditionalBranch": {
          "condition": {
            "type": "And",
            "conditions": [
              { "type": "IncomingAttackWithinTicks", "ticks": 6 },
              { "type": "DirectionTowardAttacker", "tolerance": 30 }
            ]
          },
          "ifTrue": {
            "effects": [
              { "type": "PlayAnimation", "id": "mikiri_counter", "duration": 25 },
              { "type": "AddTag", "tag": "countering", "duration": 25 },
              { "type": "AddTag", "tag": "invulnerable", "duration": 15 },
              { "type": "ApplyEffect", "target": "attacker", "effect": "posture_damage", "amount": 50 },
              { "type": "ApplyTag", "target": "attacker", "tag": "staggered", "duration": 40 },
              { "type": "FireEvent", "event": "directional_counter_success" }
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
  "attackTypeFilter": {
    "allowedTypes": ["thrust", "sweep"]
  }
}
```
