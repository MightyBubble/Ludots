# Extracted Rules, Constraints, and Principles

This document extracts all specific rules, constraints, and principles from the three architecture documents.

---

## Document 1: 空间服务_架构设计.md (Spatial Service Architecture)

### Coordinate System Rules

**Rule 1.1: Coordinate Unit - Centimeters as True Source**
- **Requirement**: World position components use **centimeters** as the true source domain (`WorldPositionCm`)
- **Constraint**: Index space (cell/chunk) is derived cache, not the source
- **Implementation**: `WorldPositionCm` is the authoritative coordinate representation

**Rule 1.2: Coordinate Type**
- **Requirement**: Uses **integer-based** centimeter units (implied by "厘米域" - centimeter domain)
- **Constraint**: No explicit mention of floating-point vs fixed-point, but integer centimeters are the true source

**Rule 1.3: SpatialCellRef as Derived Cache**
- **Requirement**: `SpatialCellRef` can only be maintained by `IndexUpdate`
- **Constraint**: Business code **MUST NOT** directly write to `SpatialCellRef`
- **Principle**: Cell references are derived from `WorldPositionCm`, not independently maintained

### Architecture Boundary Rules

**Rule 1.4: Spatial Service Does NOT Do Business Filtering**
- **Forbidden**: Spatial service does not handle business filtering semantics (faction/tags/etc.)
- **Allowed**: Business filtering is provided by upper layer token/ID
- **Boundary**: Spatial service only provides spatial queries, not semantic filtering

**Rule 1.5: Spatial Service Does NOT Implicitly Trigger Loading**
- **Forbidden**: Spatial service must not implicitly trigger chunk loading
- **Required**: Loading must be explicitly driven by AOI/Streaming
- **Principle**: Single source of truth - `LoadedChunks` drives lifecycle

**Rule 1.6: Query Output Capacity**
- **Requirement**: Query output must have **fixed capacity**
- **Requirement**: Overflow must be represented by **dropped** count (must be observable)
- **Forbidden**: Must not allocate temporary containers
- **Forbidden**: Must not implicitly expand capacity to hide problems

**Rule 1.7: Two Query Paths**
- **SpatialQuery** (Gameplay domain): Deterministic, stable ordering, deduplication, fixed capacity output
- **CullingQuery** (Presentation domain): Approximate/allowed, no sorting required, but must have fixed capacity + dropped
- **Constraint**: Culling must NOT force reuse of gameplay domain sorting/deduplication path (avoids extra n log n cost)

### Error Handling and Fail-Fast Rules

**Rule 1.8: Out-of-Bounds Position**
- **Requirement**: Must **fail-fast** (throw exception or error code)
- **Forbidden**: Must NOT silently clamp out-of-bounds positions

**Rule 1.9: Unloaded Chunk Query**
- **Requirement**: Must return explicit empty/error code
- **Forbidden**: Must NOT implicitly load chunks
- **Forbidden**: Must NOT silently fallback to full table scan

**Rule 1.10: Query Overflow**
- **Requirement**: Must be observable through **dropped** count
- **Forbidden**: Must NOT implicitly expand capacity to hide problems

### Data Model Rules

**Rule 1.11: WorldSizeSpec Immutability**
- **Requirement**: `WorldSizeSpec` is immutable after creation

**Rule 1.12: LoadedChunks as Single Source of Truth**
- **Requirement**: `LoadedChunks` is the single source of truth for which chunks are loaded
- **Requirement**: `LoadedChunks` drives spatial index and navigation/map data lifecycle
- **Forbidden**: No implicit loading or silent fallback

**Rule 1.13: Entity Structure**
```
Entity
  ├─ WorldPositionCm (true source)
  ├─ SpatialCellRef (derived cache)
  └─ Other business components
```

### Performance Budget Rules

**Rule 1.14: IndexUpdate Budget**
- **Requirement**: Index update/build per tick must have controllable upper limit
- **Implementation**: Must use cell/chunk slicing for time-sliced updates

**Rule 1.15: Query Budget**
- **Requirement**: Fixed capacity output + dropped
- **Forbidden**: Must not allocate temporary containers

**Rule 1.16: Culling Budget**
- **Forbidden**: Must not force reuse of gameplay domain sorting/deduplication path
- **Reason**: Avoids additional n log n cost

### Module Dependency Rules

**Rule 1.17: LoadedChunks Drives Lifecycle**
- **Requirement**: `LoadedChunks` (from AOI) drives spatial index lifecycle
- **Requirement**: Spatial index and navigation/map data are created/recycled based on chunk load/unload
- **Dependency**: SpatialIndex depends on LoadedChunks state

**Rule 1.18: Module Responsibilities**
- **AOI/Streaming**: Maintains `LoadedChunks`, produces enter/exit events
- **SpatialIndex**: Maintains index structure (incremental/buckets)
- **Query Service**: Exposes unified interface, responsible for output format (deterministic/limited)

### Determinism Rules

**Rule 1.19: Gameplay Domain Determinism**
- **Requirement**: Same input, same tick must produce reproducible query results
- **Requirement**: Stable ordering and fixed tie-break rules
- **Scope**: Applies to `SpatialQuery` (gameplay domain)

**Rule 1.20: Presentation Domain Approximation**
- **Allowed**: `CullingQuery` (presentation domain) can use approximation
- **Requirement**: Must still have fixed capacity + dropped for observability

---

## Document 2: 00_总览.md (Core Engine Overview)

### Core Layer Rules

**Rule 2.1: Core Layer is Pure Infrastructure**
- **Requirement**: Core layer (`src/Core/`) does NOT hold any game content
- **Requirement**: All default configuration is provided by `LudotsCoreMod`
- **Forbidden**: Core layer cannot contain game-specific systems or assets

**Rule 2.2: Extension Points Must Be Explicit**
- **Requirement**: Mod/Trigger can only inject through registry and `ContextKeys`
- **Forbidden**: No implicit extension mechanisms
- **Principle**: All extensions must be visible and traceable

**Rule 2.3: No Silent Fallback**
- **Requirement**: Configuration missing/dependency missing must **fail-fast**
- **Forbidden**: No silent fallback behavior
- **Implementation**: Must throw exceptions with clear error messages

**Rule 2.4: Observable Requirements**
- **Requirement**: Startup, map loading, critical controller state changes must be observable
- **Implementation**: Must provide events/logs/metrics entry points

**Rule 2.5: No Backward Compatibility**
- **Requirement**: Do NOT retain fallback code or deprecated interfaces
- **Forbidden**: No backward compatibility mechanisms

### Configuration Rules

**Rule 2.6: Game Constants from MergedConfig**
- **Requirement**: Game constants (`OrderTags`, `GasOrderTags`, `Attributes`) must come from `MergedConfig.Constants`
- **Forbidden**: Cannot hardcode constants in code
- **SSOT**: `MergedConfig.Constants` is the single source of truth

**Rule 2.7: Startup Map from Config**
- **Requirement**: Startup map must come from `MergedConfig.StartupMapId`
- **Forbidden**: Cannot use `MapIds.Entry` (hardcoded)

**Rule 2.8: Engine Assembly Boundary**
- **Requirement**: Engine assembly boundary is determined by `GameEngine` creation/registration flow
- **Requirement**: Any new extension points must be registered in SSOT

### Module Dependency Rules

**Rule 2.9: Core Layer Dependencies**
- **Core Layer**: Pure infrastructure, no game content
- **LudotsCoreMod**: Provides all default game content and systems
- **Other Mods**: Can override/extend through ConfigPipeline

**Rule 2.10: Config Pipeline Dependency**
- **Requirement**: All configuration must go through `ConfigPipeline`
- **SSOT**: `MergedConfig` is the single source of truth for configuration

---

## Document 3: 05_Core层解耦_裁决条款.md (Core Layer Decoupling Rules)

### Core Layer Content Rules

**Rule 3.1: Core Layer Forbidden from Holding Game Content**
- **Forbidden**: Cannot define `static class OrderTags`, `GasOrderTags`, `GameAttributes`, `MapIds` in `src/Core/`
- **Forbidden**: Cannot directly instantiate game systems in `GameEngine.InitializeCoreSystems()`
- **Forbidden**: Cannot store `Maps/`, `Entities/`, `Presentation/` assets in `assets/Configs/`
- **Allowed**: Can define engine parameters (window size, frame rate, budget)
- **Allowed**: Can define `DefaultCoreMod` pointing to default core Mod

**Rule 3.2: All Game Constants Must Come from ConfigPipeline/JSON**
- **Requirement**: `OrderTags` must be read from `MergedConfig.Constants.OrderTags`
- **Requirement**: `GasOrderTags` must be read from `MergedConfig.Constants.GasOrderTags`
- **Requirement**: `Attributes` must be read from `MergedConfig.Constants.Attributes`
- **Requirement**: `StartupMapId` must be read from `MergedConfig.StartupMapId`
- **Forbidden**: Cannot hardcode `const int MoveTo = 101;` in C# code
- **Forbidden**: Cannot use `#if` or `??` for compatibility fallback

**Rule 3.3: No Backward Compatibility or Silent Fallback**
- **Forbidden**: Cannot use `??` operator to provide default values for missing configuration
- **Forbidden**: Cannot have comments containing "backward compatibility", "fallback", "兼容" (compatibility)
- **Forbidden**: Cannot retain deprecated interface signatures
- **Requirement**: Configuration missing must fail-fast with exception clearly indicating missing item
- **Requirement**: Interface changes must directly modify all callers

**Rule 3.4: LudotsCoreMod as Sole Provider**
- **Requirement**: `LudotsCoreMod` must provide:
  - Default `game.json` (containing `constants`, `startupMapId`)
  - Default map (`assets/Maps/entry.json`)
  - Default templates (`assets/Entities/templates.json`)
  - Default HUD config (`assets/Presentation/hud.json`)
  - Core system registration (through Trigger mechanism)

### Exception Rules

**Rule 3.5: Test Code Exception**
- **Allowed**: Can define test-specific constants in `src/Tests/` (e.g., `TestGasOrderTags.cs`) for unit test isolation
- **Exit Condition**: Test constants must NOT be referenced by non-test code

**Rule 3.6: Engine Internal Constants Exception**
- **Allowed**: Can define internal constants in Core layer related to engine mechanisms (e.g., system group priority, time slice budget)
- **Exit Condition**: Internal constants must NOT be exposed to Mod or business code
- **Scope**: These constants are not "game content"

### Module Dependency Rules

**Rule 3.7: Core → LudotsCoreMod Dependency**
- **Requirement**: Core layer depends on LudotsCoreMod for all game content
- **Requirement**: Core layer cannot provide game content itself
- **Principle**: Core is infrastructure, LudotsCoreMod is content provider

**Rule 3.8: Mod Override Capability**
- **Requirement**: Mod developers can create their own CoreMod to replace LudotsCoreMod
- **Requirement**: Must support complete replacement of core framework
- **Reference**: Follows StarCraft 2 Core.SC2Mod pattern

### Validation Rules

**Rule 3.9: Validation Methods**
- **Core layer no hardcoded constants**: `grep -r "static class OrderTags" src/Core/` must return no results
- **Constants from JSON**: Check `GameEngine.InitializeCoreSystems()`
- **No fallback code**: `grep -r "backward\|fallback\|兼容" src/Core/` must return no results
- **LudotsCoreMod provides defaults**: File existence check for `src/Mods/LudotsCoreMod/assets/game.json`
- **Tests pass**: `dotnet test` must pass (118/118 passed)

---

## Summary of Key Principles

### Coordinate System
- **True Source**: Centimeters (`WorldPositionCm`) as integer-based coordinate system
- **Derived Data**: Cell/chunk references are derived cache, not source
- **No Floating-Point**: Uses integer centimeters (implied by "厘米域")

### Architecture Boundaries
- **Spatial Service**: Only spatial queries, no business filtering, no implicit loading
- **Core Layer**: Pure infrastructure, no game content
- **LudotsCoreMod**: Sole provider of game content and defaults

### Error Handling
- **Fail-Fast**: All errors must fail-fast with clear exceptions/error codes
- **No Silent Fallback**: No `??` operators, no implicit defaults, no silent clamping
- **Observable**: All overflow/errors must be observable (dropped counts, logs, metrics)

### Module Dependencies
- **LoadedChunks → SpatialIndex**: LoadedChunks drives spatial index lifecycle
- **ConfigPipeline → MergedConfig**: All configuration flows through ConfigPipeline
- **Core → LudotsCoreMod**: Core depends on LudotsCoreMod for game content

### Performance Constraints
- **Fixed Capacity**: Query outputs must have fixed capacity + dropped count
- **Budget Control**: Index updates must be time-sliced with controllable limits
- **No Temporary Allocations**: Queries must not allocate temporary containers

### Determinism
- **Gameplay Domain**: Deterministic, stable ordering, reproducible results
- **Presentation Domain**: Can use approximation but must still have fixed capacity + dropped
