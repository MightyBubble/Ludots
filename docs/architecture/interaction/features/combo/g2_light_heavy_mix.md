# G2: Light-Heavy Mix Combo

## Overview

A combo system where different button inputs (light vs heavy attack) during combo stages lead to different finishers. Combines sequential progression with branching paths based on input choice.

## User Experience

- Player presses light attack (R1) → enters combo stage 1
- Player presses light attack (R1) again → enters combo stage 2
- At any combo stage, player can press heavy attack (R2) for a different finisher:
  - R2 during stage 1 → heavy finisher A
  - R2 during stage 2 → heavy finisher B
- Each finisher has unique properties and animations

## Implementation

Same as G1, but routing depends on **different InputBindings** + combo_stage tags:

```
R1 (light) → combo_stage:1
R1 (light) with combo_stage:1 → combo_stage:2
R2 (heavy) with combo_stage:1 → heavy_finisher_A (reset combo)
R2 (heavy) with combo_stage:2 → heavy_finisher_B (reset combo)
```

**Key Difference from G1**: Multiple input bindings can query the same combo state, creating branching paths.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Automatic combo reset on timeout |
| AbilityActivationRequireTags | ⚠️ **Required** | Enable positive tag preconditions |
| Multiple InputBindings per combo | ✅ Existing | Different buttons query same combo state |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "light_1",
      "inputBinding": "R1",
      "activationRequireTags": { "none": ["combo_stage"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 50 },
          { "type": "AddTag", "tag": "combo_stage:1", "duration": 30 }
        ]
      }
    },
    {
      "id": "light_2",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["combo_stage:1"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 60 },
          { "type": "RemoveTag", "tag": "combo_stage:1" },
          { "type": "AddTag", "tag": "combo_stage:2", "duration": 25 }
        ]
      }
    },
    {
      "id": "heavy_finisher_a",
      "inputBinding": "R2",
      "activationRequireTags": { "all": ["combo_stage:1"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 150, "knockback": 5 },
          { "type": "RemoveTag", "tag": "combo_stage:1" }
        ]
      },
      "performer": "heavy_slam_animation"
    },
    {
      "id": "heavy_finisher_b",
      "inputBinding": "R2",
      "activationRequireTags": { "all": ["combo_stage:2"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 200, "aoe": 3 },
          { "type": "RemoveTag", "tag": "combo_stage:2" }
        ]
      },
      "performer": "heavy_spin_animation"
    }
  ]
}
```
