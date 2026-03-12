# N6: Meter/Counter-Based Ability Variant

## 1. User Experience

Player presses attack button, system selects special ability variant when combo meter/counter reaches threshold.

**Examples**: Arkham Special Combo Takedown (requires 8+ combo), Spider-Man finisher (requires full meter)

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 6,
  "candidates": [
    {
      "ability_id": "special_combo_takedown",
      "precondition_graph_id": 150,
      "score_graph_id": 151,
      "base_priority": 70
    },
    {
      "ability_id": "finisher_move",
      "precondition_graph_id": 152,
      "score_graph_id": 153,
      "base_priority": 65
    },
    {
      "ability_id": "standard_attack",
      "precondition_graph_id": 154,
      "score_graph_id": 155,
      "base_priority": 10
    }
  ]
}
```

**Special Combo Precondition** (ID 150):
- caster.Attribute("combo_count") >= 8
- target.HasTag("Enemy")
- distance <= 300

**Special Combo Score** (ID 151):
- score = 150.0 (very high priority when available)

**Finisher Precondition** (ID 152):
- caster.Attribute("finisher_meter") >= 100
- target.HasTag("Enemy")
- distance <= 250

**Finisher Score** (ID 153):
- score = 140.0

**Standard Attack Precondition** (ID 154):
- target.HasTag("Enemy")

**Standard Attack Score** (ID 155):
- score = 50.0

## 4. Tag/Effect/Attribute Usage

**Tags**:
- Enemy — target filter
- ComboReady — optional tag added when combo_count >= threshold (for UI feedback)
- FinisherReady — optional tag when meter full

**Attributes**:
- combo_count — incremented on hit, reset on miss/damage taken
- finisher_meter — accumulated through combat actions
- last_hit_time — for combo timeout detection

**Effects**:
- OnHit: ModifyAttribute(combo_count, +1)
- OnDamageTaken: ModifyAttribute(combo_count, 0) — reset
- Periodic: check (current_time - last_hit_time) > timeout → reset combo

## 5. Integration Points

**Input Layer**:
- Single button → ContextGroup 6
- Resolver checks attribute thresholds in precondition graphs

**GAS Layer**:
- `AttributeAggregatorSystem` maintains combo_count/finisher_meter
- Threshold checks can add ComboReady/FinisherReady tags for UI
- Effects consume meter: ModifyAttribute(finisher_meter, -100)
