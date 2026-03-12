# G4: Two-Stage Recast (Ability Reactivation)

## Overview

An ability that has a second activation form, triggered by pressing the same button again after the first stage completes or hits. Modeled after LoL-style "two-cast" abilities (e.g., Lee Sin Q). The second press uses state stored by the first.

## User Experience

- Player presses Q → launches projectile
- Projectile hits an enemy
- A short time window opens (visual indicator shows Q is "recharged")
- Player presses Q again → character dashes to the hit enemy
- Window expires → ability resets without dash

## Implementation

```
Q press 1: LaunchProjectile (skillshot mode)
  → OnHit callback (ResponseChainListener):
    → Chain effect: AddTag("q1_hit", target=hit_entity, duration=60 ticks) on caster
    → Write hit_entity to caster Blackboard (key: "q1_target")
    → AddTag("q1_available", duration=60 ticks) on caster

Q press 2:
  precondition: HasTag("q1_available")
  exec: Dash to entity stored in Blackboard["q1_target"]
        RemoveTag("q1_available")
        RemoveTag("q1_hit")
```

**Key Pattern**: Projectile OnHit writes state to caster's Blackboard and sets a time-limited "available" tag. Second press reads from Blackboard.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| ResponseChainListener (OnHit) | ✅ Existing | Trigger caster-side tag update when projectile hits |
| Blackboard write from Effect | ✅ Existing | Store hit target entity reference |
| AbilityActivationRequireTags | ⚠️ **Required** | Gate second press on "q1_available" tag |
| Tag duration (auto-expire) | ✅ Existing | Time-limit the second press window |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "q_press_1",
      "inputBinding": "Q",
      "activationBlockTags": { "blockedAny": ["q1_available"] },
      "onActivate": {
        "effects": [
          {
            "type": "LaunchProjectile",
            "projectile": "resonating_strike",
            "onHit": {
              "type": "ResponseChain",
              "applyTo": "caster",
              "effects": [
                { "type": "BlackboardWrite", "key": "q1_target", "value": "hitEntity" },
                { "type": "AddTag", "tag": "q1_available", "duration": 60 },
                { "type": "AddTag", "tag": "q1_hit", "duration": 60 }
              ]
            }
          }
        ]
      }
    },
    {
      "id": "q_press_2",
      "inputBinding": "Q",
      "activationRequireTags": { "all": ["q1_available"] },
      "onActivate": {
        "effects": [
          { "type": "DashToBlackboard", "key": "q1_target" },
          { "type": "Damage", "amount": 80 },
          { "type": "RemoveTag", "tag": "q1_available" },
          { "type": "RemoveTag", "tag": "q1_hit" }
        ]
      }
    }
  ]
}
```
