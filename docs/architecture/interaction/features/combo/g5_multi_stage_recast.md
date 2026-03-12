# G5: Multi-Stage Recast (Three or More Stages)

## Overview

Extension of G4's two-stage recast pattern to three or more sequential activations. Each press executes a different phase of the ability, with state stored between stages. Enables complex multi-part abilities like triple skillshots with follow-ups.

## User Experience

- Player presses Q → launches first phase (e.g., skillshot)
- First phase hits → second activation window opens
- Player presses Q again → executes second phase (e.g., dash to target)
- Second phase completes → third activation window opens
- Player presses Q a third time → executes finisher phase (e.g., rising kick)
- Any stage that times out resets the sequence to stage 1

## Implementation

Extension of the G4 pattern with additional stage tags:

```
Q press 1:
  precondition: NOT HasTag("q_stage")
  exec: LaunchProjectile
    → OnHit:
      AddTag("q_stage:1", duration=60 ticks) on caster
      Write hit_entity to Blackboard["q_target"]

Q press 2:
  precondition: HasTag("q_stage:1")
  exec: Dash to Blackboard["q_target"]
        RemoveTag("q_stage:1")
        AddTag("q_stage:2", duration=40 ticks)
        Damage (moderate)

Q press 3:
  precondition: HasTag("q_stage:2")
  exec: Rising kick (special animation)
        RemoveTag("q_stage:2")
        Damage (finisher, heavy)
```

**Differentiator vs G4**: Chain is longer, intermediate stages may also produce effects. Each stage advances state forward.

**Tag Duration Tuning**: Later stages typically have shorter windows (forcing faster reaction) to increase difficulty and reward skillful play.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| ResponseChainListener (OnHit) | ✅ Existing | Trigger stage advancement when projectile hits |
| Blackboard write from Effect | ✅ Existing | Store entity reference across stages |
| AbilityActivationBlockTags.RequiredAll | ✅ Existing | Gate each stage on preceding stage tag (对应现有 RequiredAll 字段) |
| Tag duration (auto-expire) | ✅ Existing | Timeout each stage window independently |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "q_stage_1",
      "inputBinding": "Q",
      "activationBlockTags": { "blockedAny": ["q_stage:1", "q_stage:2"] },
      "onActivate": {
        "effects": [
          {
            "type": "LaunchProjectile",
            "onHit": {
              "applyTo": "caster",
              "effects": [
                { "type": "BlackboardWrite", "key": "q_target", "value": "hitEntity" },
                { "type": "AddTag", "tag": "q_stage:1", "duration": 60 }
              ]
            }
          }
        ]
      }
    },
    {
      "id": "q_stage_2",
      "inputBinding": "Q",
      "activationRequireTags": { "all": ["q_stage:1"] },
      "onActivate": {
        "effects": [
          { "type": "DashToBlackboard", "key": "q_target" },
          { "type": "Damage", "amount": 60 },
          { "type": "RemoveTag", "tag": "q_stage:1" },
          { "type": "AddTag", "tag": "q_stage:2", "duration": 40 }
        ]
      }
    },
    {
      "id": "q_stage_3",
      "inputBinding": "Q",
      "activationRequireTags": { "all": ["q_stage:2"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 140, "isFinisher": true },
          { "type": "RemoveTag", "tag": "q_stage:2" }
        ]
      },
      "performer": "rising_kick_animation"
    }
  ]
}
```
