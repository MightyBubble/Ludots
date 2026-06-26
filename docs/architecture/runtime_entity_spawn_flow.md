# Runtime Entity Spawn Flow

Runtime spawn requests are the single path for creating gameplay entities at runtime. GAS handlers such as unit creation and projectile launch enqueue spawn requests; the runtime spawn system materializes ECS components through the same authoring and manifestation contracts used elsewhere.

## Flow

```text
ability/order handler
  -> RuntimeEntitySpawnRequest
  -> RuntimeEntitySpawnSystem
  -> authored component data
  -> manifestation and physics projection
  -> gameplay/runtime entity
```

## Rules

- Handlers do not directly bypass spawn contracts to create special-case entities.
- Manifestation obstacle authoring remains the source for obstacle geometry.
- Physics projection owns collider and rigid body state.
- MassFlow consumes obstacle snapshots through the current navigation-domain execution contracts.

## Current References

- `gitbook/reference/obstacle-authoring.md`
- `gitbook/reference/mass-navigation-execution-avoidance-and-targets.md`
- `docs/architecture/gas_combat_infrastructure.md`
