# G9: Dodge Attack

## Overview

A special attack that is only available immediately following a dodge/roll. The dodge action sets a short-lived tag on the caster, and a follow-up attack ability reads this tag as its activation requirement. Rewards skillful dodge usage with a bonus offensive option.

## User Experience

- Player presses dodge/roll → character performs evasive roll
- A brief window opens after dodge completes (~0.25s)
- Player presses attack during the window → executes dodge attack (e.g., rolling slash)
- Player misses the window → attack executes as normal attack instead
- Dodge attack typically has different properties: lower damage, repositioning benefit, or surprise element

## Implementation

```
Dodge/Roll ability:
  onComplete → AddTag("post_dodge", duration=15 ticks)

Dodge Attack ability:
  precondition: HasTag("post_dodge")
  exec: dodge attack animation + damage
  onActivate: RemoveTag("post_dodge")

Normal Attack ability:
  precondition: NOT HasTag("post_dodge") [optional — or just lower priority]
  exec: standard attack
```

**Routing**: Both dodge_attack and normal_attack are bound to the attack button. `OrderSubmitter` checks preconditions — if `post_dodge` tag is present, dodge_attack takes priority.

**Optional**: Normal attack can still execute when `post_dodge` is absent; dodge_attack simply wins via precondition when the tag exists.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Limit the dodge-attack window to 15 ticks |
| AbilityExecSpec onComplete event | ✅ Existing | Trigger post_dodge tag at dodge completion |
| AbilityActivationBlockTags.RequiredAll | ✅ Existing | Gate dodge_attack on "post_dodge" tag (对应现有 RequiredAll 字段) |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "dodge_roll",
      "inputBinding": "Circle",
      "onActivate": {
        "effects": [
          { "type": "Displacement", "direction": "input", "distanceCm": 300 },
          { "type": "AddTag", "tag": "invincible", "duration": 8 }
        ]
      },
      "onComplete": {
        "effects": [
          {
            "type": "AddTag",
            "tag": "post_dodge",
            "duration": 15,
            "comment": "~250ms dodge-attack window"
          }
        ]
      },
      "performer": "roll_animation"
    },
    {
      "id": "dodge_attack",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["post_dodge"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 70, "applyFrom": "behindTarget" },
          { "type": "RemoveTag", "tag": "post_dodge" }
        ]
      },
      "performer": "rolling_slash_animation"
    },
    {
      "id": "normal_attack",
      "inputBinding": "R1",
      "activationBlockTags": { "blockedAny": ["post_dodge"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 50 }
        ]
      },
      "performer": "standard_slash_animation"
    }
  ]
}
```
