# N5: Environment-Based Ability Variant

## 1. User Experience

Player presses attack button, system selects different ability based on environmental context (near wall, near throwable object, near ledge, etc.).

**Examples**: Arkham environmental takedowns, Spider-Man throwing objects, wall-slam attacks

## 2. Interaction Model Mapping

- **InputConfig**: ReactsTo: Down
- **TargetMode**: Unit or Point
- **Acquisition**: ContextScored
- **TargetFilter**: Enemy or Environment

## 3. Implementation Strategy

**ContextGroup Configuration**:
```json
{
  "context_group_id": 5,
  "candidates": [
    {
      "ability_id": "wall_slam",
      "precondition_graph_id": 140,
      "score_graph_id": 141,
      "base_priority": 45
    },
    {
      "ability_id": "throw_object",
      "precondition_graph_id": 142,
      "score_graph_id": 143,
      "base_priority": 40
    },
    {
      "ability_id": "ledge_takedown",
      "precondition_graph_id": 144,
      "score_graph_id": 145,
      "base_priority": 50
    },
    {
      "ability_id": "standard_attack",
      "precondition_graph_id": 146,
      "score_graph_id": 147,
      "base_priority": 10
    }
  ]
}
```

**Wall Slam Precondition** (ID 140):
- caster.HasTag("NearWall")
- target distance <= 300
- target.HasTag("Enemy")

**Wall Slam Score** (ID 141):
- score = 90.0 + (wall_proximity_bonus * 10)

**Throw Object Precondition** (ID 142):
- SpatialQuery finds entity with tag "Throwable" within 150cm
- target.HasTag("Enemy") within 800cm

**Throw Object Score** (ID 143):
- score = 80.0 + (object_proximity_bonus * 5)

**Ledge Takedown Precondition** (ID 144):
- caster.HasTag("NearLedge")
- target distance <= 200
- target.HasTag("Enemy")

**Ledge Takedown Score** (ID 145):
- score = 100.0 (highest priority for dramatic effect)

## 4. Tag/Effect/Attribute Usage

**Tags**:
- NearWall — caster environmental state
- NearLedge — caster environmental state
- Throwable — environment object tag
- Enemy — target filter

**Attributes**:
- None (pure tag + spatial query)

**Effects**:
- Wall slam: Displacement toward wall + impact damage
- Throw object: pickup + projectile throw
- Ledge takedown: displacement + fall damage

## 5. Integration Points

**Input Layer**:
- Single button → ContextGroup 5
- Resolver performs spatial queries for environment entities

**Environment System**:
- Proximity detection system maintains NearWall/NearLedge tags on entities
- Environment objects tagged as Throwable/Interactable
