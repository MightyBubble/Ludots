# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Ludots is a high-performance, data-oriented C# game framework built on Arch ECS. Targets complex game genres (MOBA, RTS, Simulation) with deterministic fixed-point math, a modular "everything is a mod" architecture, and a Gameplay Ability System (GAS) inspired by Unreal Engine. Licensed under AGPL-3.0.

## Build & Test Commands

```bash
# Build the main Raylib app
dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release

# Run all GAS tests
dotnet test src/Tests/GasTests/GasTests.csproj

# Run all tests verbose
dotnet test src/Tests/GasTests/GasTests.csproj --logger "console;verbosity=detailed"

# Run a specific test class
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~TagRuleSetTests"

# Run architecture boundary tests
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj

# ModLauncher CLI: build mods, write game.json, run
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli mods build --mods "MobaDemoMod"
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --mods "MobaDemoMod"
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run
```

Target framework: .NET 8.0. Test framework: NUnit 4.2.2 with BenchmarkDotNet.

## Architecture

### Source Layout

- `src/Core/` — Engine core (ECS, GAS, physics, navigation, modding, config). No platform dependencies.
- `src/Apps/Raylib/` — Desktop app entry point (`Program.cs` → `RaylibGameHost`)
- `src/Adapters/Raylib/` — Host loop, dependency composition
- `src/Client/Ludots.Client.Raylib/` — Raylib input/rendering adapters
- `src/Mods/` — 20+ built-in and demo mods (each with `mod.json`)
- `src/Tools/` — ModLauncher (CLI/GUI), Editor Bridge, NavBake
- `src/Libraries/` — Source-integrated: Arch ECS, Arch.Extended, DotRecast, Raylib-cs
- `src/Tests/` — GasTests (core gameplay), ArchitectureTests (boundaries), ModdingTest
- `docs/developer-guide/` — 11 detailed architecture guides (Chinese)

### Hexagonal Architecture (Adapter Pattern)

Core has zero dependencies on platform libraries. Interfaces (`IInputBackend`, `IRenderBackend`) live in Core; platform-specific adapters (Raylib) implement them. Data translation happens at the boundary: `Fix64Vec2` ↔ `float`, `ResourceHandle` ↔ `Texture2D`.

### ECS (Arch)

- All components must be **blittable structs** — no reference types (`string`, `class`, `List<T>`)
- **Zero-GC** in core loop — no managed heap allocations in system `Update`
- Cache `QueryDescription` as `private readonly` fields; never create in hot loop
- Use `CommandBuffer` for structural changes (Create/Destroy/Add/Remove); replay at phase boundaries, never inside query loops
- Deterministic: use `Fix64`/`Fix64Vec2` for gameplay state, never `float`; no `System.Random`; no dictionary iteration order dependency

### SystemGroup Execution Order (GameEngine.cs)

```
SchemaUpdate → InputCollection → PostMovement → AbilityActivation →
EffectProcessing → AttributeCalculation → DeferredTriggerCollection →
Cleanup → EventDispatch → ClearPresentationFlags
```

All systems must belong to exactly one group. Execution order within phases is deterministic.

### Gameplay Ability System (GAS)

- Ability activation produces `EffectRequest` — never modifies world state directly
- Effect lifecycle phases: `OnPropose → OnResolve → OnApply → OnRemove`
- Each phase has sub-segments: `Pre → Main → Post → Listeners`
- Cross-layer data flows through **Sinks** (`AttributeSinkRegistry`), not direct component writes
- Sink pattern centralizes type conversion (float↔Fix64), write strategy, and clock domain decoupling

### Modding System

- **"Everything is a Mod"** — Core itself is mounted as a mod
- `mod.json` manifest: `name` (unique ModId), `version`, optional `main` (DLL path), `dependencies`, `priority`
- ModLoader: scans → parses manifests → topological sort by dependencies → VFS mount → DLL load → `OnLoad(ModContext)`
- VFS paths: `ModId:Path/To/Resource` (e.g., `Core:Configs/game.json`)
- ConfigPipeline merges `game.json` from Core + mods in load order; objects merge recursively, arrays/scalars override

### Startup Flow

1. App reads `game.json` (only `ModPaths` — bootstrap, not runtime config)
2. `GameBootstrapper.InitializeFromBaseDirectory` → VFS mount Core → ModLoader → ConfigPipeline merge
3. ECS World creation → system registration in SystemGroup order → presentation init
4. `engine.Start()` → `engine.LoadMap(startupMapId)` → main loop (`engine.Tick(dt)`)

## Naming Conventions

- **Data components**: suffix `Cm` (e.g., `WorldPositionCm`) or plain noun (`Velocity`)
- **Tag components**: suffix `Tag` (e.g., `IsPlayerTag`, `IsDeadTag`)
- **Event components**: suffix `Event` (e.g., `CollisionEvent`)

## Testing Conventions

Tests follow AAA (Arrange/Act/Assert). Test class: `<Subsystem>Tests`. Test method: `<Subject>_<Scenario>_<Expected>`.

- Use NUnit: `Assert.That(actual, Is.EqualTo(expected))`
- Each test gets its own `World` — `using var world = World.Create();` — no cross-test sharing
- Clean static registries (`TagOps.ClearRuleRegistry()`) in `[TearDown]`/`finally`
- No `Console.WriteLine` — use `TestContext.WriteLine` only for failure diagnostics
- No LINQ in GAS hot-path tests; `GameplayEventBus.Events` via index access (`for` + `events[i]`), not `foreach`
- See `src/Tests/GasTests/TESTING_STYLE.md` for full rules

## Key Documentation

| Guide | File |
|-------|------|
| Documentation standards | `docs/developer-guide/00_documentation_standards.md` |
| ECS & SoA principles | `docs/developer-guide/01_ecs_soa_principles.md` |
| Mod architecture & VFS | `docs/developer-guide/02_mod_architecture.md` |
| Adapter pattern | `docs/developer-guide/03_adapter_pattern.md` |
| CLI guide | `docs/developer-guide/04_cli_guide.md` |
| Pacemaker (time/timestep) | `docs/developer-guide/05_pacemaker.md` |
| Presentation & Performers | `docs/developer-guide/06_presentation_performer.md` |
| ConfigPipeline merging | `docs/developer-guide/07_config_pipeline.md` |
| Trigger system | `docs/developer-guide/08_trigger_guide.md` |
| Startup entrypoints | `docs/developer-guide/09_startup_entrypoints.md` |
| Map/Mod spatial services | `docs/developer-guide/10_map_mod_spatial.md` |
| GAS layered architecture & Sinks | `docs/developer-guide/11_gas_layered_architecture.md` |
