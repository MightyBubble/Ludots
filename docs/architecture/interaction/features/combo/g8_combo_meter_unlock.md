# G8: Combo Meter Unlock

## Overview

A resource-gated special ability that becomes available only after the player has landed a sufficient number of consecutive hits. Represented as an attribute (`combo_meter`) that increments on hit and resets on being hit. Once the threshold is reached, a powerful finisher ability unlocks.

## User Experience

- Player lands hits → combo counter increments (visible in UI)
- Player gets hit → combo counter resets to 0
- After N consecutive hits, special attack button lights up
- Player activates special attack → executes finisher + resets counter to 0
- If player never reaches threshold, finisher remains unavailable

## Implementation

```
combo_meter Attribute (on caster):
  Each hit landed → combo_meter += 1
    (via OnHit ResponseChain: ModifyAttribute(combo_meter, +1))
  Each hit received → combo_meter = 0
    (via OnReceiveHit ResponseChain: SetAttribute(combo_meter, 0))

special_combo ability:
  precondition: combo_meter >= threshold (e.g., 10)
    → Expressed via Tag: periodic effect checks attribute → adds "combo_ready" tag
  exec: special finisher attack
  onComplete: SetAttribute(combo_meter, 0) or RemoveTag("combo_ready")
```

**Attribute-to-Tag Bridge Pattern**:
```
PeriodicCheck Effect (runs every tick):
  Graph:
    LoadSelfAttribute(combo_meter)
    CompareFloat(>= threshold)
    JumpIfFalse              B[0], @no_combo
    // true → already has tag or will get it via GrantedTags on this effect
    WriteBlackboardInt       E[0], "combo_status", 1
    Jump                     @end
  @no_combo:
    WriteBlackboardInt       E[0], "combo_status", 0
  @end:
    // combo_status → 驱动 Tag 授予/移除（通过 GrantedTags + Lifetime）
```

This bridges the attribute (numeric value) to the tag precondition system.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| ResponseChainListener (OnHit) | ✅ Existing | Increment combo_meter on successful hit |
| ResponseChainListener (OnReceiveHit) | ✅ Existing | Reset combo_meter when caster is hit |
| Attribute system (combo_meter) | ✅ Existing | Persistent numeric combo counter |
| Graph attribute comparison ops | ✅ Existing | Evaluate threshold condition |
| Ability precondition based on Attribute threshold | P1 | Direct attribute-threshold gate (alternative to Tag bridge) |
| AbilityActivationBlockTags.RequiredAll | ✅ Existing | Gate finisher on "combo_ready" tag (对应现有 RequiredAll 字段) |

## Configuration Example

```json
{
  "attributes": [
    { "id": "combo_meter", "defaultValue": 0, "min": 0, "max": 100 }
  ],
  "periodicEffects": [
    {
      "id": "combo_meter_watcher",
      "period": 1,
      "isAlwaysActive": true,
      "graphId": "check_combo_threshold"
    }
  ],
  "graphs": {
    "check_combo_threshold": {
      "ops": [
        { "op": "LoadSelfAttribute", "attribute": "combo_meter" },
        { "op": "CompareFloat", "gte": 10 },
        { "op": "JumpIfFalse", "target": "@no_combo" },
        { "op": "WriteBlackboardInt", "entity": "self", "key": "combo_status", "value": 1 },
        { "op": "Jump", "target": "@end" },
        { "label": "@no_combo", "op": "WriteBlackboardInt", "entity": "self", "key": "combo_status", "value": 0 },
        { "label": "@end" }
      ]
    }
  },
  "abilities": [
    {
      "id": "special_finisher",
      "inputBinding": "Triangle",
      "activationRequireTags": { "all": ["combo_ready"] },
      "onActivate": {
        "effects": [
          { "type": "Damage", "amount": 300, "isSpecial": true },
          { "type": "SetAttribute", "attribute": "combo_meter", "value": 0 },
          { "type": "RemoveTag", "tag": "combo_ready" }
        ]
      },
      "performer": "special_finisher_animation"
    }
  ]
}
```
