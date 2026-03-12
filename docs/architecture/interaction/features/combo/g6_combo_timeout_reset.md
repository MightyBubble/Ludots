# G6: Combo Timeout Reset

## Overview

The mechanism by which combo stages automatically expire when the player inputs too slowly. Time-limited tags on the caster define the valid window between combo presses. When a tag expires, the combo returns to its initial state.

## User Experience

- Player lands first attack → has ~0.5 seconds to press again
- Player presses within the window → combo continues
- Player waits too long → combo resets; next press starts from stage 1
- Window duration can vary per stage (later stages may be stricter or more lenient)

## Implementation

`combo_stage:N` tags are given a duration equal to the combo window in game ticks. The existing `EffectLifetimeSystem` automatically removes expired tags.

```
combo_stage:N Tag duration = window time (e.g., 25-30 ticks)
On expire: tag is removed → state reads "no combo_stage tag" = reset to stage 0
Already existing: EffectLifetimeSystem auto-clears expired tags
```

**No new code needed.** This is a pure configuration mechanism.

**Window Tuning Considerations**:
- `combo_stage:1` (first hit window): ~30 ticks (0.5s at 60 Hz) — generous, allows reaction time
- `combo_stage:2` (second hit window): ~25 ticks — slightly stricter
- Later stages: can tighten further for challenge-rewarding design

**Tick-to-Millisecond Conversion**: At 60 Hz fixed-step, 1 tick = ~16.7ms. 30 ticks ≈ 500ms.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Core mechanism — no new code needed |
| EffectLifetimeSystem | ✅ Existing | Processes tag expirations every tick |
| combo_stage tags (G1-G5) | Design-time | Tags whose duration defines the window |

## Configuration Example

```json
{
  "comboConfig": {
    "stage1": {
      "tag": "combo_stage:1",
      "windowTicks": 30,
      "comment": "~500ms at 60Hz"
    },
    "stage2": {
      "tag": "combo_stage:2",
      "windowTicks": 25,
      "comment": "~416ms, slightly tighter"
    },
    "stage3": {
      "tag": "combo_stage:3",
      "windowTicks": 20,
      "comment": "~333ms, expert timing required"
    }
  },
  "example_ability_with_timeout": {
    "id": "light_attack_1",
    "inputBinding": "R1",
    "onActivate": {
      "effects": [
        { "type": "Damage", "amount": 50 },
        {
          "type": "AddTag",
          "tag": "combo_stage:1",
          "duration": 30
        }
      ]
    }
  }
}
```
