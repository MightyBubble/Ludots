# N4: Target-State-Based Ability Variant

## 1. User Experience

Player presses attack button, system selects different ability based on target's current state (knocked down, stunned, armed, etc.).

**Examples**: Arkham ground finisher on downed enemies, Sekiro deathblow on posture-broken enemies

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 4,
  "candidates": [
    {
      "ability_id": "ground_finisher",
      "precondition_graph_id": 130,
      "score_graph_id": 131,
      "base_priority": 50
    },
    {
      "ability_id": "posture_break_finisher",
      "precondition_graph_id": 132,
      "score_graph_id": 133,
      "base_priority": 60
    },
    {
      "ability_id": "disarm_attack",
      "precondition_graph_id": 134,
      "score_graph_id": 135,
      "base_priority": 40
    },
    {
      "ability_id": "standard_attack",
      "precondition_graph_id": 136,
      "score_graph_id": 137,
      "base_priority": 10
    }
  ]
}
```

**Ground Finisher Precondition** (ID 130):
- target.HasTag("KnockedDown")
- distance <= 200

**Ground Finisher Score** (ID 131):
- score = 100.0

**Posture Break Precondition** (ID 132):
- target.HasTag("PostureBroken")
- distance <= 150

**Posture Break Score** (ID 133):
- score = 120.0 (highest priority)

**Disarm Precondition** (ID 134):
- target.HasTag("Armed")
- distance <= 250

**Disarm Score** (ID 135):
- score = 80.0

## 4. Tag/Effect/Attribute Usage

**Tags**:
- KnockedDown — target state
- PostureBroken — target state (from posture attribute threshold)
- Armed — target state
- Stunned — target state
- Enemy — target filter

**Attributes**:
- posture — accumulates damage, threshold triggers PostureBroken tag

**Effects**:
- Ground finisher: high damage + execution animation
- Posture break: instant kill or massive damage
- Disarm: remove weapon + damage

## 5. Integration Points

**Input Layer**:
- Single button → ContextGroup 4
- Resolver prioritizes finisher opportunities

**GAS Layer**:
- Posture system: `AttributeAggregatorSystem` checks threshold → adds PostureBroken tag
- Knockdown effects add KnockedDown tag with duration
