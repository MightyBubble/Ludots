# G1: Light Attack Chain (Animation Progression)

## Overview

A sequential combo system where repeated presses of the same button execute different attacks in progression. Each stage has its own animation and properties, with automatic reset after timeout.

## User Experience

- Player presses attack button (e.g., R1) repeatedly
- First press: executes light attack animation 1
- Second press (within time window): executes light attack animation 2
- Third press (within time window): executes light attack animation 3 (finisher)
- If player waits too long between presses, combo resets to stage 1

## Implementation

Requires 3 separate `AbilityDefinition` instances bound to the same `InputBinding`:

```
light_attack_1:
  precondition: NOT HasTag("combo_stage")
  exec: damage + AddTag("combo_stage:1", duration=30 ticks)
  performer: swing_animation_1

light_attack_2:
  precondition: HasTag("combo_stage:1")
  exec: damage + RemoveTag("combo_stage:1") + AddTag("combo_stage:2", duration=25 ticks)
  performer: swing_animation_2

light_attack_3:
  precondition: HasTag("combo_stage:2")
  exec: heavy_damage + RemoveTag("combo_stage:2")
  performer: swing_animation_3
```

**Routing Logic**: Same `InputBinding` → `OrderSubmitter` checks preconditions → activates matching ability.

**Timeout Reset**: `combo_stage:N` tags have duration (25-30 ticks). When expired, `EffectLifetimeSystem` automatically removes them, resetting to stage 0.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Automatic combo reset on timeout |
| AbilityActivationRequireTags | ⚠️ **Required** | Enable positive tag preconditions (currently only BlockTags exist) |
| ContextGroup routing | Optional | Alternative routing for same binding → multiple abilities |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "light_attack_1",
      "inputBinding": "R1",
      "activationRequireTags": { "none": ["combo_stage"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 50 },
          { "type": "AddTag", "tag": "combo_stage:1", "duration": 30 }
        ]
      },
      "performer": "swing_animation_1"
    },
    {
      "id": "light_attack_2",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["combo_stage:1"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 60 },
          { "type": "RemoveTag", "tag": "combo_stage:1" },
          { "type": "AddTag", "tag": "combo_stage:2", "duration": 25 }
        ]
      },
      "performer": "swing_animation_2"
    },
    {
      "id": "light_attack_3",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["combo_stage:2"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 100 },
          { "type": "RemoveTag", "tag": "combo_stage:2" }
        ]
      },
      "performer": "swing_animation_3"
    }
  ]
}
```
