# N3: Self-State-Based Ability Variant

## 1. User Experience

Player presses attack button, system selects different ability based on caster's current state (airborne, grounded, on wall, etc.).

**Examples**: Spider-Man aerial combo vs ground combo, Dark Souls aerial plunge attack

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit or None
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy (if Unit mode)

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 3,
  "candidates": [
    {
      "ability_id": "aerial_attack",
      "precondition_graph_id": 120,
      "score_graph_id": 121,
      "base_priority": 30
    },
    {
      "ability_id": "ground_attack",
      "precondition_graph_id": 122,
      "score_graph_id": 123,
      "base_priority": 10
    },
    {
      "ability_id": "wall_attack",
      "precondition_graph_id": 124,
      "score_graph_id": 125,
      "base_priority": 25
    }
  ]
}
```

**Aerial Attack Precondition** (ID 120):
- caster.HasTag("Airborne")
- !caster.HasTag("Grounded")

**Aerial Attack Score** (ID 121):
- score = 100.0 (high priority when airborne)

**Ground Attack Precondition** (ID 122):
- caster.HasTag("Grounded")

**Ground Attack Score** (ID 123):
- score = 50.0

**Wall Attack Precondition** (ID 124):
- caster.HasTag("OnWall")

**Wall Attack Score** (ID 125):
- score = 90.0

## 4. Tag/Effect/Attribute Usage

**Tags**:
- Airborne — self state
- Grounded — self state
- OnWall — self state
- Enemy — target filter

**Attributes**:
- None (pure tag-based routing)

**Effects**:
- Aerial: downward strike + landing impact
- Ground: standard combo
- Wall: wall-kick attack

## 5. Integration Points

**Input Layer**:
- Single button → ContextGroup 3
- Resolver checks caster tags first (fast path)

**State Management**:
- Physics/movement systems maintain Airborne/Grounded/OnWall tags
- Tag updates trigger ability availability changes
