# G7: Hit Confirm Continue

## Overview

A combo system where progression to the next stage requires the previous attack to have actually landed on an enemy. If the swing misses, the combo sequence resets rather than advancing. Rewards accurate play and prevents combo spam.

## User Experience

- Player presses attack → executes swing
- Swing hits an enemy → visual confirmation + second stage becomes available
- Player presses again → combo continues from stage 2
- Swing misses → no confirmation → next press restarts from stage 1
- Fundamentally different from timeout (G6): timeout is about speed, hit-confirm is about accuracy

## Implementation

```
Hit1:
  exec: swing attack (damage effect with ResponseChainListener)
  OnHit callback (ResponseChainListener triggers when damage resolves):
    → Chain effect: AddTag("hit_confirmed", duration=20 ticks) on caster

Hit2:
  precondition: HasTag("hit_confirmed")
  → If Hit1 missed, "hit_confirmed" tag does not exist → Hit2 rejected → combo resets
  exec: second stage attack + RemoveTag("hit_confirmed")
```

**Contrast with G6 (timeout)**: G6 resets on slow input. G7 resets on missed attacks. Both can be combined: require both hit confirmation AND timely follow-up.

**Combined G6+G7 Pattern**:
```
OnHit: AddTag("hit_confirmed", duration=20 ticks)  // expires if no follow-up
Hit2 precondition: HasTag("hit_confirmed")          // requires both conditions
```

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| ResponseChainListener (OnHit) | ✅ Existing | Detect successful hits and trigger tag addition |
| Tag duration (auto-expire) | ✅ Existing | Auto-reset hit-confirm tag if no follow-up |
| AbilityActivationRequireTags | ⚠️ **Required** | Gate Hit2 on "hit_confirmed" tag |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "hit_confirm_stage_1",
      "inputBinding": "R1",
      "activationBlockTags": { "blockedAny": ["hit_confirmed"] },
      "onActivate": {
        "effects": [
          {
            "type": "Damage",
            "amount": 60,
            "onHit": {
              "applyTo": "caster",
              "effects": [
                {
                  "type": "AddTag",
                  "tag": "hit_confirmed",
                  "duration": 20,
                  "comment": "~333ms window at 60Hz"
                }
              ]
            }
          }
        ]
      },
      "performer": "slash_animation_1"
    },
    {
      "id": "hit_confirm_stage_2",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["hit_confirmed"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 90 },
          { "type": "RemoveTag", "tag": "hit_confirmed" }
        ]
      },
      "performer": "slash_animation_2"
    }
  ]
}
```
