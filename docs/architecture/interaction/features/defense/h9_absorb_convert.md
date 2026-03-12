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

> ⚠️ 以下配置使用 Ludots 标准 EffectTemplate + Graph Phase 格式，替换了原虚构 DSL。

```json5
// === Effect Templates (mods/<yourMod>/Effects/absorb_convert_effects.json) ===
[
  {
    // 吸收姿态 Buff：按住期间持续存在，授予 absorbing Tag
    // 松开按键后由 Ability block.End 移除
    "id": "Effect.Absorb.ShieldBuff",
    "presetType": "Buff",
    "lifetime": "UntilRemoved",
    "grantedTags": [
      { "tag": "Status.Absorbing", "formula": "Fixed", "amount": 1 }
    ],
    "configParams": {
      "MitigationCoeff": { "type": "float", "value": 0.2 },
      "ConversionRatio": { "type": "float", "value": 0.5 },
      "ConversionCapPerHit": { "type": "float", "value": 50.0 }
    },
    "phaseListeners": [
      {
        // 减伤 + 转化 Listener：拦截伤害并转化为能量
        "phase": "OnApply",
        "eventTagId": "incoming_damage",
        "priority": 100,
        "scope": "Self",
        "graphProgramId": "Graph.Absorb.MitigateAndConvert"
      }
    ]
  }
]

// === Graph Program: Graph.Absorb.MitigateAndConvert ===
// 在 OnApply phase 拦截伤害，减伤后将部分伤害转化为 Energy 属性
//
//   ReadBlackboardFloat     E[effect], DamageAmount       → F[0]   // 原始伤害
//   LoadConfigFloat         "MitigationCoeff"             → F[1]   // 0.2
//   MulFloat                F[0], F[1]                    → F[2]   // 减伤后伤害
//   WriteBlackboardFloat    E[effect], FinalDamage, F[2]           // 写回减伤后伤害
//
//   // 转化计算: convertedEnergy = min(originalDamage * ratio, cap)
//   LoadConfigFloat         "ConversionRatio"             → F[3]   // 0.5
//   MulFloat                F[0], F[3]                    → F[4]   // 转化量
//   LoadConfigFloat         "ConversionCapPerHit"         → F[5]   // 50.0
//   MinFloat                F[4], F[5]                    → F[6]   // 限制上限
//   LoadContextTarget       E[1]                                   // 吸收者自身
//   ModifyAttributeAdd      E[effect], E[1], Energy, F[6]         // 增加 Energy
//
//   SendEvent               "damage_absorbed"                      // 触发 VFX/音效

// === AbilityExecSpec: 吸收护盾（Hold 模式）===
// 使用 HeldPolicy=StartEnd，Down 施加 Buff，Up 移除 Buff
{
  "id": "Ability.Absorb.Shield",
  "exec": {
    "totalTicks": 0,
    "items": [
      // block.Start: 施加吸收 Buff
      { "kind": "EffectSignal", "tick": 0, "effectId": "Effect.Absorb.ShieldBuff" }
    ]
  }
  // block.End 由 Ability 的 HeldPolicy=StartEnd 自动触发 Effect 移除
}

// === InputOrderMapping ===
{
  "actionId": "ability_Block",
  "trigger": "Held",
  "heldPolicy": "StartEnd",
  "orderTypeKey": "castAbility",
  "selectionType": "None",
  "isSkillMapping": true,
  "castModeOverride": "SmartCast"
}

// === Attribute 定义 ===
// Energy 属性（P1 需求）：
//   { "id": "Energy", "defaultValue": 0, "min": 0, "max": 200 }
//   { "id": "EnergyDecayPerTick", "defaultValue": 0.5 }
//   { "id": "EnergyDecayDelayTicks", "defaultValue": 120 }
// Energy 衰减通过 Attribute Regen 系统配置（已有基建）。

// === 护盾破碎（可选）===
// 复用 H1 的 GuardBreakSystem 模式：
//   累计吸收伤害通过 GuardStamina 属性追踪
//   GuardStamina ≤ 0 时强制移除 Effect.Absorb.ShieldBuff
//   施加 Status.GuardBroken Tag（冷却 90 ticks）
```
