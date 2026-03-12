# G11: Channel Segmented Fire

## Overview

A channeled ability where the player holds one button to maintain a channeling state (e.g., aiming mode) and presses another button multiple times to fire discrete shots during the channel. Modeled after abilities like Jhin's ultimate (R) in LoL. Each shot consumes part of the channel's budget (e.g., 4 shots max).

## User Experience

- Player presses and holds channel button (e.g., R) → enters channeling mode
- Camera zooms, character locks in place, aiming cone appears
- Player aims with cursor/stick and presses fire button (e.g., left-click or R again)
- First shot fires in aimed direction
- Player re-aims and presses fire again → second shot
- After 4 shots (or channel timeout), channel ends automatically
- Player can cancel early by releasing channel button

## Implementation

```
Channel ability (R):
  AbilityExecSpec:
    Item[0]: TagClip "channeling", "cone_aiming" (duration=MaxChannelTime)
    Item[1]: InputGate @ tick 0 (wait for fire input)
    Item[2]: EffectSignal → shot_1 (read current aim direction from Blackboard)
    Item[3]: InputGate (wait for next fire input)
    Item[4]: EffectSignal → shot_2
    Item[5]: InputGate
    Item[6]: EffectSignal → shot_3
    Item[7]: InputGate
    Item[8]: EffectSignal → shot_4 (final shot, extra damage)
    Item[9]: RemoveTag("channeling")
```

**Key Components**:
- `InputGate`: Pauses ability execution until player provides input (fire button press)
- `EffectSignal`: Fires a projectile using current cursor direction from Blackboard
- Cursor direction is continuously written to Blackboard while "channeling" tag is active (via `InputOrderMappingSystem`)

**Already Existing**: `InputGate` in `AbilityExecSpec` is confirmed to exist.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| InputGate in AbilityExecSpec | ✅ Existing | Pause execution until player input |
| Cursor direction continuous write | P1 (Gap Analysis) | Update aim direction every frame during channel |
| TagClip (channeling) | ✅ Existing | Mark entity as channeling for input routing |
| EffectSignal | ✅ Existing | Fire projectile at current aim direction |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "jhin_ultimate",
      "inputBinding": "R",
      "execSpec": {
        "items": [
          {
            "type": "TagClip",
            "tags": ["channeling", "cone_aiming", "rooted"],
            "duration": 600,
            "comment": "10 seconds max channel time"
          },
          {
            "type": "InputGate",
            "waitForInput": "fire",
            "timeout": 600
          },
          {
            "type": "EffectSignal",
            "effectId": "curtain_call_shot_1",
            "readDirectionFrom": "blackboard.cursor_direction"
          },
          {
            "type": "InputGate",
            "waitForInput": "fire",
            "timeout": 600
          },
          {
            "type": "EffectSignal",
            "effectId": "curtain_call_shot_2"
          },
          {
            "type": "InputGate",
            "waitForInput": "fire",
            "timeout": 600
          },
          {
            "type": "EffectSignal",
            "effectId": "curtain_call_shot_3"
          },
          {
            "type": "InputGate",
            "waitForInput": "fire",
            "timeout": 600
          },
          {
            "type": "EffectSignal",
            "effectId": "curtain_call_shot_4",
            "comment": "Final shot with bonus damage"
          },
          {
            "type": "RemoveTag",
            "tags": ["channeling", "cone_aiming", "rooted"]
          }
        ]
      }
    }
  ],
  "effects": {
    "curtain_call_shot_1": {
      "type": "LaunchProjectile",
      "damage": 100,
      "speed": 2000,
      "range": 5000
    },
    "curtain_call_shot_4": {
      "type": "LaunchProjectile",
      "damage": 200,
      "speed": 2000,
      "range": 5000,
      "comment": "Final shot deals double damage"
    }
  }
}
```
