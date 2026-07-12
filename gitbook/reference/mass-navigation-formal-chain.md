# MassNavigation Formal Chain

Current status: formal Selection APIs are retired. MassNavigation command authority flows through
`EntityCollectionStore` and the default `collection.command.source` collection, then into explicit
`OrderBuffer` orders. New code/config must not add `SelectionRuntime`, `SelectionSetKeys`,
`selection.live.primary`, or selected-provider fallback.

## Reference Implementation

- Core runtime: `src/Core/MassNavigation/`
- Capability assets: `mods/capabilities/navigation/MassNavigationMod/`
- Formation business consumer: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/`
- User-facing tutorial: `gitbook/reference/mass-navigation-user-book.md`

## Responsibility Boundary

| Layer | Owns | Must Not Own |
| --- | --- | --- |
| Core | Config pipeline, MassNavigation agent binding, MassNavigationFlow simulation, explicit spatial-target ingestion, MovePlanning execution sink, ECS writeback, collection events, performer/minimap integration | Formation identity, membership, slots, facing, rotation input, formation-specific orders or presentation |
| MassNavigationMod | Asset/config package and optional UI adapter | Core binding/runtime/order ingestion or any formation business state |
| FormationCapabilityShowcaseMod | Scenario config, formation identity and membership, slot layout, facing, `formationMove` / `formationRotate` orders, explicit per-member target production, player-readable showcase UI | Private Selection runtime, private config loader, private MassNavigation binding runtime or direct solver access |

## End-To-End Chain

```mermaid
flowchart TD
    Launch["Raylib launch graph"] --> Config["ConfigPipeline"]
    Config --> GameJson["game.json"]
    Config --> Map["formation_capability_showcase.json"]
    Config --> NavConfig["MassNavigationConfig.json"]
    Config --> ShowcaseConfig["FormationCapabilityShowcaseConfig.json"]
    Config --> Templates["Entities/templates.json"]
    Config --> Performers["Presentation/performers.json"]

    ShowcaseConfig --> Runtime["FormationCapabilityShowcaseRuntime"]
    Templates --> SpawnQueue["RuntimeEntitySpawnQueue"]
    Runtime --> SpawnQueue
    SpawnQueue --> SpawnSystem["RuntimeEntitySpawnSystem"]
    SpawnSystem --> World["Authored ECS entities"]
    World --> BindingGroup["SystemGroup.RuntimeEntityBinding"]
    BindingGroup --> AgentBinding["MassNavigationAuthoredAgentBindingSystem"]
    BindingGroup --> EnvBinding["MassNavigationEnvironmentBindingSystem"]
    BindingGroup --> ShowcaseBinding["FormationCapabilityScenarioBindingSystem"]

    AgentBinding --> Agents["MassNavigation agents"]
    EnvBinding --> Obstacles["MassNavigation blockers / hot zones"]
    ShowcaseBinding --> Formations["Showcase-owned formation state"]

    GenericOrders["OrderBuffer(massNavigationMove)"] --> Ingestion["MassNavigationOrderIngestionSystem"]
    Ingestion --> Groups["MassNavigationGroupRuntime"]
    Groups --> Solver["MassNavigationFlowSolverState"]

    CommandSource["EntityCollectionStore(collection.command.source)"] --> FormationOrders["OrderBuffer(formationMove / formationRotate)"]
    FormationOrders --> FormationOrderSystem["FormationCapabilityOrderSystem"]
    FormationOrderSystem --> FormationTargets["Showcase slot and facing policy"]
    FormationTargets --> Intent["MovePlanExecutionIntent"]
    Intent --> Sink["MassNavigationMovePlanExecutionSink"]
    Sink --> Solver
    Solver --> EcsState["WorldPositionCm / FacingDirection"]
    EcsState --> PerformerSync["Performer transform sync"]
    Performers --> PerformerRules["PerformerRuleSystem"]
    PerformerRules --> PerformerRuntime["PerformerRuntimeSystem"]
```

## Command Source To Marker

Command-source marker lifecycle is driven by collection events and performer rules. It is not a
private MassNavigation subsystem.

```mermaid
flowchart LR
    Player["Player acquisition or revoke"] --> Store["EntityCollectionStore"]
    Store --> Event["EntityCollectionMemberAdded / EntityCollectionMemberRemoved"]
    Event --> Rules["PerformerRuleSystem"]
    Rules --> Runtime["PerformerRuntimeSystem"]
    Runtime --> Marker["Scoped command marker performer"]
```

Configuration event keys use `collection.command.source`; code uses
`EntityCollectionKeys.CommandSource`. Do not add alternate spellings, compatibility aliases,
showcase-only command-source keys, or Selection fallback.

## Order Chain

```text
Local input
  -> EntityCollectionStore(collection.command.source)
  -> OrderBuffer(formationMove / formationRotate)
  -> FormationCapabilityOrderSystem
  -> showcase-owned command state and slot policy
  -> MovePlanExecutionIntent
  -> MassNavigationMovePlanExecutionSink
  -> MassNavigationFlowSolverState
  -> ECS position/facing handoff
  -> performer sync
```

MassNavigation core does not read `CommandSource`, `InteractionContextStack`, or retired Selection
authority APIs. Generic `massNavigationMove` accepts one explicit spatial destination and no formation
payload. Formation-facing input and formation geometry stay in the consuming Mod; MassNavigation only
executes the explicit targets that reach its Order or MovePlanning seams.

## Spawn Chain

```text
FormationCapabilityShowcaseRuntime
  -> RuntimeEntitySpawnQueue
  -> RuntimeEntitySpawnSystem
  -> authored ECS components
  -> SystemGroup.RuntimeEntityBinding
  -> MassNavigationAuthoredAgentBindingSystem
  -> MassNavigationEnvironmentBindingSystem
  -> FormationCapabilityShowcaseScenarioBindingSystem
  -> showcase-owned formation components and preallocated member ranges
```

Formation is not a MassNavigation Core feature. Loading MassNavigation without the Formation
Capability Showcase must not register formation components, orders, slot state, rotation state, or
follower systems.

## Config Rules

- Use semantic order keys in assets; runtime ids are implementation details.
- Keep command marker definition ids in command-source terminology.
- Keep obstacle authoring in map/template ECS components.
- Keep formation identity, membership, slots, facing, rotation and presentation inside the showcase mod.
- Use `MovePlanExecutionIntent` / `MassNavigationMovePlanExecutionSink` for explicit member targets;
  do not access `MassNavigationFlowSolverState` or `MassNavigationGroupRuntime` from the showcase.
- Keep MassNavigation capability assets reusable by other mods.
