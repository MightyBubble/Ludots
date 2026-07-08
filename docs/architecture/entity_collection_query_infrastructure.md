# Entity Collection Query Infrastructure

Status: Current architecture.

Selection retirement boundary:

- Formal `SelectionRuntime`, `SelectionSetKeys`, `SelectionViewKeys`, `SelectionContextRuntime`, `SelectionControlGroupRuntime`, `OrderSelectionReference`, `SelectionRequest`, and `SelectionResponse` are retired as official selection APIs.
- User-facing copy may still say "selection" as shorthand for the default command source, but runtime authority is `EntityCollectionStore`.
- The default commandable set is `(owner, EntityCollectionKeys.CommandSource)` / `collection.command.source`.
- UI acquisition may keep a preview collection, but committed command intent writes `collection.command.source`; it must not fall back to `SelectionRuntime`.
- Presentation rules use `EntityCollectionMemberAdded` / `EntityCollectionMemberRemoved` with collection keys such as `collection.command.source`.

## Scope

Entity collections are reusable query/display state for ordered sets of entities. They are the authoritative home for command-source sets, and they do not replace domain-specific ECS state such as ownership, teams, or relationships.

The infrastructure exists so UI acquisition previews, EntityInfo inspectors, command panels, spatial query results, GAS graph results, debug views, and future derived collections can share one high-performance store instead of each feature keeping a private entity list.

## Ownership

The shared store is:

- `src/Core/EntityCollections/EntityCollectionStore.cs`
- `src/Core/EntityCollections/EntityCollectionTypes.cs`
- `CoreServiceKeys.EntityCollectionStore`
- `CoreServiceKeys.EntityCollectionKeyRegistry`

`GameEngine` creates the string key registry and the store during core service setup. Callers address a collection by `(owner entity, collection key)`. The owner is the context that owns the query/display state, not necessarily a selected entity.

## Data Model

Collections are stored as SoA arrays:

- collection metadata arrays: owner, key id, source kind, role, context entity, primary entity, revision, signature, row start/count
- row arrays: entity, ordinal, role id, row flags
- dictionary index arrays keyed by owner identity and key id

The store exposes narrow operations:

- `Replace(...)` writes an entire collection and increments revision only when the descriptor or rows change.
- `TryGet(...)` and `TryGetView(...)` resolve metadata without copying rows.
- `CopyEntities(...)`, `CopyWindow(...)`, `TryGetEntityAt(...)`, and `TryGetRowAt(...)` read row data into caller-provided spans.

Hot reads are span-based and do not allocate after capacity warmup. Growth is explicit array growth inside the store, not per-query object churn.

## Source And Role Semantics

`EntityCollectionSourceKind` describes where rows came from:

- `Explicit`
- `UiAcquisition`
- `CollectionView`
- `CollectionSnapshot`
- `RelationDerived`
- `SpatialQuery`
- `GasGraphResult`
- `Debug`

`EntityCollectionRoleKind` describes how consumers should treat the rows:

- `Display`
- `AcquisitionPreview`
- `CommandSource`
- `Debug`

These are descriptors for collection consumers. They are not permission checks and they do not create a second source of truth.

## Command Source Boundary

`EntityCollectionStore` is the authoritative model for reusable entity sets. The default command source is `EntityCollectionKeys.CommandSource` (`collection.command.source`).

UI click and box acquisition write acquisition results into `EntityCollectionStore`. Replace/add/toggle command-source mutations publish the committed actor set to `(owner, collection.command.source)` with `EntityCollectionRoleKind.CommandSource`.

This separation is intentional:

- UI acquisition is input/query state.
- `collection.command.source` is the gameplay-facing default command set.
- Display/query collections can be inspected or sampled without mutating command authority.

Missing collection services or missing collection keys must fail explicitly at the consuming boundary. Silent fallback to `SelectionRuntime`, `SelectionSetKeys`, `SelectionContextRuntime`, `SelectionControlGroupRuntime`, or old viewed-selection globals is forbidden.

## EntityInfo Boundary

EntityInfo can target an explicit entity collection through `EntityInfoPanelTargetKind.EntityCollection`.

The panel service resolves the collection from `EntityCollectionStore`, copies a window into its existing sampled panel state, and renders rows through the same insight profile, text catalog, and `UIRoot` path used by entity panels. Inspecting a collection does not mutate viewed selection.

Reusable EntityInfo templates are presentation descriptors over the same sampling path. A missing template id fails explicitly; templates do not introduce a second profile system or text system.

## Command Panel Boundary

The command panel has a context source dispatch layer:

- `IEntityCommandPanelContextSource`
- `IEntityCommandPanelContextActionSource`
- `EntityCommandPanelSourceDispatch`

The collection-backed GAS source is registered as `gas.collection-ability-slots`. It resolves a registered query config through `IEntityCommandPanelCollectionQueryConfigRegistry`, reads owners from `EntityCollectionStore`, aggregates ability slots through the existing GAS source, filters/sorts by config, and activates through the existing GAS action source.

Unknown query ids fail with an explicit error. The source does not reinterpret an unknown instance key as a collection key.

## Query Config

Command collection queries are configured with:

- `Id`
- `CollectionKey`
- `Title`
- `Filter`
- `Sort`

Filter kinds cover any, ready, blocked, active, ability id, and action id. Sort kinds cover slot, owner count, label, ability id, and status-oriented ordering.

The default command-source query uses `EntityCollectionKeys.CommandSource` and sorts deterministically by slot, owner count, and label.

## Performance Rules

Hot path rules:

- Use `CopyWindow` or `CopyEntities` with caller-owned spans.
- Do not enumerate rows through LINQ or allocate per row.
- Do not use `Enum.HasFlag` inside collection aggregation loops; use bit operations.
- Warm capacity before large benchmark loops when measuring steady-state query cost.
- Treat UI retained tree allocation as a presentation-layer cost, not collection-query runtime cost.

Benchmarks cover:

- replacing a 100k-row collection and copying 64-row windows
- aggregating command slots from a 128-owner command-source collection with zero allocation after warmup

## Verification

Primary tests:

- `src/Tests/GasTests/EntityCollectionStoreTests.cs`
- `src/Tests/GasTests/EntityCollectionQueryBenchmarkTests.cs`
- `src/Tests/GasTests/EntityCommandPanelCollectionQueryTests.cs`
- `src/Tests/GasTests/InteractionSelectionConvergenceTests.cs`
- `src/Tests/PresentationTests/EntityInfoPanelServiceTests.cs`

Related docs:

- `docs/architecture/entity_selection_architecture.md`
- `docs/architecture/entity_insight_panel_architecture.md`
- `docs/architecture/entity_command_panel_infrastructure.md`
- `docs/architecture/ecs_soa.md`
