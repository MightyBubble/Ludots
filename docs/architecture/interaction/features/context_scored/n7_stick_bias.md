# N7: Stick Direction Bias

## 1. User Experience

Player pushes movement stick toward a distant enemy while pressing attack, system biases target selection toward that direction (e.g., triggers lunge to farther enemy instead of closer one).

**Examples**: Arkham pushing stick toward distant enemy + attack = flying tackle

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 7,
  "candidates": [
    {
      "ability_id": "directional_lunge",
      "precondition_graph_id": 160,
      "score_graph_id": 161,
      "base_priority": 30
    },
    {
      "ability_id": "standard_attack",
      "precondition_graph_id": 162,
      "score_graph_id": 163,
      "base_priority": 10
    }
  ]
}
```

**Directional Lunge Precondition** (ID 160):
- input_direction magnitude > 0.3 (stick pushed)
- target distance >= 200 && distance <= 800
- target.HasTag("Enemy")

**Directional Lunge Score** (ID 161):
```
input_dir = normalize(input_vector)
to_target_dir = normalize(target.position - caster.position)
alignment = dot(input_dir, to_target_dir)  // -1 to 1

score = 50.0 * alignment  // 0-50 bonus for aligned targets
score += (distance / 800) * 30  // 0-30 bonus for farther targets
```

**Standard Attack Score** (ID 163):
- score = 40.0 - (distance / 300) * 20  // closer = higher score

## 4. Tag/Effect/Attribute Usage

**Tags**:
- Enemy — target filter

**Attributes**:
- 内建打分用的是 `HoveredBiasScore`。摇杆方向不是属性。

**Effects**:
- Directional lunge: Displacement + damage on arrival

## 5. Integration Points

**Input Layer**:
- 槽号经 `SubmitCast` 进入 `CommandIntentBufferDrainSystem`，再由 `ContextScoredOrderResolver` 打分
- 摇杆采样在控制方案的轴上。要按摇杆方向偏置，写在候选的 score graph 里，不在排干系统里写方向黑板

**Score Graph**:
- Graph ops: LoadBlackboardFloat, CalcDirection, DotProduct
- Alignment calculation biases selection toward stick direction
