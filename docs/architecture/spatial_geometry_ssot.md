# Spatial Geometry SSOT

## Scope

This document defines the Core ownership rule for 2D spatial geometry that can be consumed by obstacle blocking, physics collision, navigation, GAS targeting or hit regions, screen-space projection, and selection acquisition.

The rule is intentionally semantic: Core may let several consumers share the same authored shape data, but a consumer-specific runtime component must not become an accidental global truth for every other consumer.

## Architecture Rule

Core separates authored geometry intent from derived runtime state.

- Authored semantic contracts are the source of truth for their own domain.
- Derived runtime components are caches or lower-level materialization, not authoring truth.
- Consumers may share the same authored source only through an explicit shared profile or generation policy.
- Divergence is valid only when it is named, data-driven, validated, and testable.
- Adapters consume or import Core contracts; adapters do not infer hidden product geometry truth.

## Current Contracts

| Contract | Role | Ownership |
| --- | --- | --- |
| `SpatialBounds` | authored projection/query envelope selector | Core spatial authoring |
| `SpatialBox3D` | authored local 3D projection/query box | Core spatial authoring |
| `SpatialFootprint2D` | authored local 2D projection/query polygons | Core spatial authoring |
| `ManifestationObstacleIntent2D` | authored single obstacle intent for runtime manifestations | Core gameplay authoring |
| `ManifestationObstaclePolygon2D` | authored single polygon payload for obstacle intent | Core gameplay authoring |
| `CompoundObstacle2D` | authored compound obstacle intent for one logical entity | Core gameplay authoring |
| `Collider2D` | derived physics collider state | Physics2D runtime |
| `NavObstacle2D` | derived navigation obstacle state | Navigation runtime |
| `CompoundObstacle2DState` | derived materialized obstacle slots | Physics2D/navigation bridge runtime |
| `SelectionSelectableTag` | selection eligibility marker | Selection runtime |
| `SelectionAcquisitionMode` | input acquisition policy | Selection runtime |

## Selection Rule

Selection must not define a low-level geometry truth.

Selection acquisition combines:

- eligibility: `SelectionSelectableTag`, `SelectionSelectableState`, and `SelectionEligibility`
- generic projection geometry: `SpatialBounds`, `SpatialBox3D`, `SpatialFootprint2D`, and `SpatialBoundsUtility`
- acquisition policy: replace, additive, toggle, and deterministic tie-breaking

Do not add components named like `SelectionFootprint2D`, `SelectionRange`, or `SelectionBounds` at the spatial layer. Those names leak UI/input semantics into generic spatial data and create a parallel SSOT.

## Obstacle Rule

Obstacle authoring is not selection authoring.

`ManifestationObstacleIntent2D` and `CompoundObstacle2D` express blocking intent. They may materialize into physics colliders and navigation obstacles, but they do not automatically define hit, hurt, target, selection, or presentation projection areas.

If content wants selection and obstacle geometry to match, both consumers must reference or be generated from the same authored shape profile in the authoring pipeline. If content wants them to differ, the divergence must be explicit. Examples:

- shared profile: one authored polygon set generates both `SpatialFootprint2D` and `CompoundObstacle2D`
- explicit override: obstacle uses compound pieces, selection uses a projected box
- derived hull: damage/hurt region is generated as a convex hull of obstacle pieces
- projected bounds policy: pointer selection uses a screen-space projection envelope
- consumer filter: GAS targeting uses the same spatial query shape but filters teams/tags differently

## Data Flow

Authoring flows through normal Core config and registry paths:

```text
map/template JSON
  -> ConfigPipeline / ComponentRegistry
  -> authored ECS components
  -> consumer bridge or query utility
  -> derived runtime state
```

For obstacles:

```text
CompoundObstacle2D or ManifestationObstacleIntent2D
  -> ManifestationObstacleBridge2DSystem
  -> ShapeDataStorage2D
  -> Collider2D / NavObstacle2D / CompoundObstacle2DState
```

For selection:

```text
SelectionSelectableTag + SpatialBounds/SpatialFootprint2D
  -> CurrentSelectionApplySystem
  -> SpatialBoundsUtility
  -> SelectionRuntime container mutation
```

For GAS hit or target regions:

```text
effect/order authoring
  -> target resolver / spatial query strategy
  -> gameplay filters and effect dispatch
```

## Validation Requirements

Core loaders must reject:

- missing required authored shape payloads
- unsupported shape kinds
- polygons with fewer than 3 or more than the documented maximum vertices
- ambiguous coordinate fields such as object-form and split local offset fields authored together
- missing sink intent when a contract requires at least one consumer sink
- duplicate or conflicting authored geometry identifiers once reusable geometry profiles are introduced

Runtime bridge systems must fail explicitly when derived state cannot be materialized. They must not silently fall back to point bounds, hidden child entities, or adapter-private geometry.

## Performance Requirements

Hot runtime components stay fixed-size and value-oriented.

- ECS components must not contain unbounded arrays or lists.
- Per-frame spatial query and selection paths must avoid LINQ and allocation-heavy enumeration.
- Derived multi-shape state should use fixed slots or compact indices into lower-level storage.
- Authoring loaders may use richer JSON objects, but they must lower into compact runtime data.

## Future Work

Issue #132 should be reframed as selection acquisition over generic spatial geometry, not as a request for selection-owned geometry components.

Reusable geometry profiles may be introduced later, but they must be generic. A suitable vocabulary would be `GeometryProfile2D`, `GeometryUse`, and explicit consumer bindings, not selection- or obstacle-branded hidden truth.
