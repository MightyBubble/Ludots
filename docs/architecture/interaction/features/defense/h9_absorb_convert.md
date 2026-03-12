# H9: Absorb / Convert to Resource (Block → Energy Gain)

## Overview

A defensive mechanic where blocking or absorbing incoming damage converts a portion of it into a beneficial resource (energy, mana, shield charge, etc.). The player is incentivized to intentionally take hits while in a defensive stance to fuel offensive abilities. Reference: OW Zarya absorbing damage for energy, DS blocking to regain mana.

## User Experience

- Player activates a defensive stance or shield (e.g., hold L1)
- Incoming attacks hit the shield and deal reduced or zero damage
- Each absorbed hit converts a percentage of the damage into a resource (e.g., energy bar fills)
- The accumulated resource can be spent on enhanced attacks or abilities
- The conversion rate may have a cap per hit or per time window to prevent abuse

## Implementation

The absorb ability applies a `absorbing` tag while active. Incoming damage is intercepted, reduced, and a portion is converted to a resource via a custom effect:

```
absorb_shield:
  inputBinding: L1 (hold)
  onHoldStart: AddTag("absorbing", duration=∞)
               + PlayAnimation("shield_up")
  onHoldEnd:   RemoveTag("absorbing")

on_incoming_damage:
  precondition: HasTag("absorbing")
  effect: DamageMultiplier(0.2)   # 80% reduction
          + ConvertDamageToResource(ratio=0.5, resource="energy", cap=50)
          + FireEvent("damage_absorbed")

energy_resource:
  max: 200
  decayPerTick: 0.5
  decayDelayTicks: 120
```

**Conversion logic**: `ConvertDamageToResource(ratio, resource, cap)` takes the original incoming damage value, multiplies it by the ratio (e.g., 0.5 = 50% conversion), and adds the result to the specified resource. The `cap` parameter limits the maximum gain per hit.

**Resource decay**: The accumulated resource decays over time if not used, encouraging the player to spend it on abilities.

**Shield break**: If cumulative absorbed damage exceeds a threshold (similar to H1 guard-break), the `absorbing` tag is forcibly removed and a cooldown is applied.

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| Tag hold/release lifecycle | ✅ Existing | Apply/remove `absorbing` tag on input transitions |
| IncomingDamage modifier hook | ✅ Existing | Intercept damage and apply multiplier when tag present |
| ConvertDamageToResource effect | ⚠️ **Required** | Convert absorbed damage to a custom resource |
| Resource component (energy, mana, etc.) | ⚠️ **Required** | Track accumulated resource with max, decay, and consumption |
| FireEvent("damage_absorbed") | ✅ Existing | Trigger VFX, audio, and UI feedback |
| Shield break threshold | Optional | Limit total absorbable damage before forced cooldown |

## Configuration Example

```json
{
  "abilities": [
    {
      "id": "absorb_shield",
      "inputBinding": "L1",
      "inputMode": "Hold",
      "onHoldStart": {
        "effects": [
          { "type": "AddTag", "tag": "absorbing" },
          { "type": "PlayAnimation", "id": "shield_up" }
        ]
      },
      "onHoldEnd": {
        "effects": [
          { "type": "RemoveTag", "tag": "absorbing" }
        ]
      }
    }
  ],
  "passives": [
    {
      "id": "absorb_conversion",
      "trigger": "OnIncomingDamage",
      "precondition": { "all": ["absorbing"] },
      "effects": [
        { "type": "DamageMultiplier", "value": 0.2 },
        { "type": "ConvertDamageToResource", "ratio": 0.5, "resource": "energy", "capPerHit": 50 },
        { "type": "FireEvent", "event": "damage_absorbed" }
      ]
    }
  ],
  "resources": {
    "energy": {
      "max": 200,
      "decayPerTick": 0.5,
      "decayDelayTicks": 120
    }
  },
  "shieldBreak": {
    "threshold": 500,
    "cooldownTicks": 90
  }
}
```
