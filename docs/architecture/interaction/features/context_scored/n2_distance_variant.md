# N2: Distance-Based Ability Variant

## 1. User Experience

Player presses attack button, system selects different ability variants based on distance to target (e.g., far distance triggers lunge/dash attack, close distance triggers standard punch).

**Examples**: Arkham far distance flying tackle vs close melee punch

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy
- **Range**: Variable per variant

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 2,
  "candidates": [
    {
      "ability_id": "lunge_attack",
      "precondition_graph_id": 110,
      "score_graph_id": 111,
      "base_priority": 20
    },
    {
      "ability_id": "melee_punch",
      "precondition_graph_id": 112,
      "score_graph_id": 113,
      "base_priority": 10
    }
  ]
}
```

**Lunge Attack Precondition** (ID 110):
- distance >= 300 && distance <= 800
- target.HasTag("Enemy")
- !target.HasTag("Dead")

**Lunge Attack Score** (ID 111):
- score = distance >= 300 ? 50.0 : 0.0

**Melee Punch Precondition** (ID 112):
- distance < 300
- target.HasTag("Enemy")

**Melee Punch Score** (ID 113):
- score = distance < 300 ? 30.0 : 0.0

## 4. Tag/Effect/Attribute Usage

**Tags**:
- Enemy — target filter
- Dead — exclusion

**Attributes**:
- None (distance-based routing only)

**Effects**:
- Lunge: Displacement + Damage on arrival
- Melee: Immediate damage

## 5. Integration Points

**Input Layer**:
- Single button binding → ContextGroup 2
- `ContextScoredResolver` evaluates all candidates, selects highest score

**GAS Layer**:
- Each variant is a separate `AbilityDefinition`
- Standard execution after resolution
