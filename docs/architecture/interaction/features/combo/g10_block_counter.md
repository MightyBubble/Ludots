# G10: Block Counter

## Overview

A special counterattack that is only available immediately following a successful block. When the player blocks an incoming attack, a short window opens during which a counter ability can be activated. Rewards defensive play with offensive opportunities.

## User Experience

- Player holds block button → enters blocking stance
- Enemy attacks → player successfully blocks
- A brief window opens after block (~0.25-0.5s)
- Player presses attack during the window → executes counter (e.g., riposte, parry strike)
- Player misses the window → attack executes as normal attack instead
- Counter attack typically has bonus properties: guaranteed hit, stun, or extra damage

## Implementation

```
Block ability:
  onActivate: AddTag("blocking")
  onReceiveHit (while blocking):
    → ResponseChain: AddTag("post_block", duration=15-30 ticks) on caster

Counter Attack ability:
  precondition: HasTag("post_block")
  exec: counter animation + damage + stun
  onActivate: RemoveTag("post_block")

Normal Attack ability:
  precondition: NOT HasTag("post_block") [optional]
  exec: standard attack
```

**Routing**: Similar to G9 (dodge attack). Both counter_attack and normal_attack are bound to the attack button. `OrderSubmitter` checks preconditions — if `post_block` tag is present, counter_attack takes priority.

**Block Detection**: The block ability grants a "blocking" tag. When the caster receives damage while "blocking" is active, a ResponseChain on the damage effect adds the "post_block" tag.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag duration (auto-expire) | ✅ Existing | Limit the counter window to 15-30 ticks |
| ResponseChainListener (OnReceiveHit) | ✅ Existing | Detect successful block and trigger post_block tag |
| AbilityActivationRequireTags | ⚠️ **Required** | Gate counter_attack on "post_block" tag |
| Conditional ResponseChain (only if blocking) | ✅ Existing | Graph precondition in ResponseChain |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "block_stance",
      "inputBinding": "L1",
      "heldPolicy": "StartEnd",
      "onActivate": {
        "effects": [
          { "type": "AddTag", "tag": "blocking" },
          { "type": "ModifyAttribute", "attribute": "damage_reduction", "op": "Override", "value": 0.8 }
        ]
      },
      "onDeactivate": {
        "effects": [
          { "type": "RemoveTag", "tag": "blocking" }
        ]
      },
      "onReceiveHit": {
        "precondition": { "hasTag": "blocking" },
        "effects": [
          {
            "type": "AddTag",
            "tag": "post_block",
            "duration": 20,
            "comment": "~333ms counter window"
          }
        ]
      },
      "performer": "block_stance_animation"
    },
    {
      "id": "counter_attack",
      "inputBinding": "R1",
      "activationRequireTags": { "all": ["post_block"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 120, "guaranteedHit": true },
          { "type": "AddTag", "tag": "stunned", "duration": 30, "applyTo": "target" },
          { "type": "RemoveTag", "tag": "post_block" }
        ]
      },
      "performer": "riposte_animation"
    },
    {
      "id": "normal_attack",
      "inputBinding": "R1",
      "activationBlockTags": { "blockedAny": ["post_block"] },
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
