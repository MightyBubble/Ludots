# N1: Auto-Select Melee Target

## 1. User Experience

Player presses attack button, system automatically selects the optimal nearby melee target and executes the attack without manual targeting.

**Examples**: Arkham attack, Spider-Man attack, God of War attack

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy
- **Range**: Melee range (configurable, typically 150-300cm)

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 1,
  "candidates": [
    {
      "ability_id": "melee_attack",
      "precondition_graph_id": 100,
      "score_graph_id": 101,
      "base_priority": 10
    }
  ]
}
```

**Scoring Factors**:
- Distance (weight: 0.4) — closer targets score higher
- Angle (weight: 0.3) — targets in front score higher
- Target state (weight: 0.3) — attacking/vulnerable targets score higher

**Precondition Graph** (ID 100):
- Check distance <= melee_range
- Check target has Enemy tag
- Check target not dead

**Score Graph** (ID 101):
- score = (1.0 - distance/max_range) * 0.4
- score += (1.0 - angle/180) * 0.3
- score += target.HasTag("Attacking") ? 0.2 : 0.0
- score += target.HasTag("Vulnerable") ? 0.1 : 0.0

## 4. Tag/Effect/Attribute Usage

**Tags**:
- Enemy — target filter
- Attacking — scoring bonus
- Vulnerable — scoring bonus
- Dead — precondition exclusion

**Attributes**:
- None required (pure spatial + tag scoring)

**Effects**:
- OnActivate: damage/knockback effects (standard ability execution)

## 5. Integration Points

**Input Layer**:
- `InputOrderMappingSystem.HandleContextScored()` invokes `ContextScoredResolver.TryResolve()`

**GAS Layer**:
- `AbilityExecSystem` receives resolved (ability, target) pair
- Standard activation flow (check RequiredAll/BlockedAny, execute OnActivate effects)

**Spatial Query**:
- `ISpatialQueryService.QueryCircle(caster.position, melee_range, Enemy)` collects candidates
