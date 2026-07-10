# Structure Collision Surfaces

SSOT: [GitHub issue #591](https://github.com/MightyBubble/Ludots/issues/591).

This page is an implementation index only. Do not copy the full design, Gherkin
acceptance spec, red lines, or scope decisions here; update issue #591 instead
and keep this page limited to code and test entry points.

## Runtime Entry Points

- `src/Core/StructureCollision/StructureCollisionAsset.cs`
- `src/Core/StructureCollision/StructureCollisionAssetBuilder.cs`
- `src/Core/StructureCollision/StructureCollisionAssetJson.cs`
- `src/Core/StructureCollision/GroundSurfaceSampler.cs`
- `src/Core/StructureCollision/StructureCollisionAdapters.cs`
- `src/Core/StructureCollision/StructureGroundingBenchmark.cs`
- `src/Core/Engine/GameEngine.MapLoadLifecycle.cs`
- `src/Core/Map/MapSession.cs`

## Authored Asset And Tests

- Fixture: `assets/StructureCollision/issue591_structure_collision.scoll.json`
- Acceptance tests: `src/Tests/ArchitectureTests/StructureCollisionIssue591Tests.cs`

Current coverage includes:

- cooked chunked SoA loading and internal span validation
- map load fail-fast for declared structure-aware grounding/navigation
- strict named enum and flag parsing for authored JSON
- half-open structure shape bounds at chunk boundaries
- terrain plus structure grounding, bridge/ramp/platform policies, and agent masks
- movement/projectile/physics/debug derived views
- dirty chunk invalidation and fail-fast output span sizing
- ideal grid and non-ideal overlap/long-span grounding benchmarks

## Scope Note

The current #591 delivery is structure collision asset infrastructure:
asset loading, chunked queries, terrain-plus-structure grounding, and derived
adapter views for navigation, physics, selection, camera, and debug consumers.

Real MassNavigation or Physics2D runtime consumption must be designed and
implemented through their owning pipelines. Do not add a parallel blocker truth
or an ad hoc bridge from this page; track that work from issue #591 or a
follow-up issue before treating the epic as closed.
