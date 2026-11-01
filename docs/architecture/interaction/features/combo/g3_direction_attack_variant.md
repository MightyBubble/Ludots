# G3: Direction + Attack Variant

## Overview

Different attack abilities are executed based on directional input combined with attack button. Enables context-sensitive moves like jump attacks (forward+attack) or backstep attacks (back+attack).

## User Experience

- Player holds directional input + presses attack button
- Forward + R2 → jump attack (aerial strike)
- Back + R1 → backstep attack (retreat slash)
- Neutral + R1 → standard attack
- Direction is evaluated at the moment of button press

## Implementation

**Option A: ArgsTemplate with InputDirection**

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


**Option B: ContextScored Routing**

```
ContextGroup evaluates input direction via scoring:
  score_factor: input_direction_dot_forward > 0.7 → jump_attack
  score_factor: input_direction_dot_forward < -0.7 → backstep_attack
  score_factor: input_magnitude < 0.1 → neutral_attack
```

**Recommended**: Option A for deterministic mapping, Option B for complex scoring with multiple factors (enemy position, terrain, etc.).

## Dependencies

| Component | Status | Purpose |
|-----------|--------|---------|
| InputDirection in ArgsTemplate | ✅ Existing | Pass directional input to ability system |
| ContextScored routing | P1 (Gap Analysis) | Dynamic ability selection based on context |
| Input direction dot product scoring | P2 | Evaluate directional input in scoring graphs |

## Configuration Example

**Option A: ArgsTemplate Approach**

```json
{
  "inputMappings": [
    {
      "actionId": "HeavyAttack",
      "binding": "R2",
      "orderTypeKey": "castAbility",
      "argsTemplate": {
        "abilitySlot": "heavy_attack_default"
      },
      "directionalVariants": [
        {
          "direction": "forward",
          "threshold": 0.7,
          "argsTemplate": { "abilitySlot": "jump_attack" }
        },
        {
          "direction": "back",
          "threshold": -0.7,
          "argsTemplate": { "abilitySlot": "backstep_attack" }
        }
      ]
    }
  ]
}
```

**Option B: ContextScored Approach**

```json
{
  "contextGroups": [
    {
      "id": "directional_attacks",
      "candidates": [
        {
          "abilityId": "jump_attack",
          "scoreGraphId": "score_forward_attack",
          "basePriority": 100
        },
        {
          "abilityId": "backstep_attack",
          "scoreGraphId": "score_backward_attack",
          "basePriority": 100
        },
        {
          "abilityId": "neutral_attack",
          "scoreGraphId": "score_neutral_attack",
          "basePriority": 50
        }
      ]
    }
  ],
  "scoreGraphs": {
    "score_forward_attack": {
      "ops": [
        { "op": "LoadInputDirection" },
        { "op": "DotProduct", "vector": [0, 1] },
        { "op": "Threshold", "min": 0.7, "score": 100, "else": 0 }
      ]
    }
  }
}
```
