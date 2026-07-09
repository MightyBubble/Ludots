# Mod Extensible Runtime

This page is the SSOT for the current user-extensible runtime surface: split config files, GAS extension points, graph ops, and Performer extension points.

## Overview

Ludots keeps Core behavior stable and lets Mods add variants by registering semantic keys during startup, then composing those keys through existing config assets. A Mod should add graph wiring, effect steps, performer rules, or shard files. It should not require a new Core enum for every variant.

The runtime has three hard rules:

- Registration happens only during `IMod.OnLoad`.
- Config compilation happens only after the extension hub is frozen.
- Duplicate extension keys, missing catalog entries, missing shards, unknown extension keys, and missing handler tables fail fast.

## Structure

| Area | Authoring surface | Runtime owner |
|------|-------------------|---------------|
| Config shards | `config_catalog.json` with `ShardDirectories` | `ConfigPipeline` |
| Effect handler code | `IModContext.Extensions.Gas.RegisterBuiltinHandler` | `BuiltinHandlerRegistry` |
| Graph op code | `IModContext.Extensions.Gas.RegisterGraphOp` | `GasGraphOpRegistry` and `GasGraphOpHandlerTable` |
| Effect composition | `GAS/effects.json`, `GAS/preset_types.json`, `GAS/graphs.json` | GAS loaders and graph compiler |
| Performer commands | `IModContext.Extensions.Presentation.RegisterPerformerCommand` | `PerformerCommandKindRegistry` |
| Performer behaviors | `IModContext.Extensions.Presentation.RegisterPerformerBehavior` | `PerformerBehaviorKindRegistry` |

Mods receive only `IModExtensionRegistration`. The mutable `ModExtensionHub` stays internal to the engine startup path.
After the hub freezes, every Mod-facing registration method rejects new keys. Re-registering the same Mod key is also an error; Mods must treat semantic keys as single-owner declarations, not as idempotent runtime writes.

## Details

### Config shards

Each formal config loader must resolve its file through `ConfigPipeline.RequireEntry`. A catalog entry declares its merge policy, id field, optional shard directories, and whether empty results are allowed.

Example:

```json
{
  "Path": "GAS/abilities.json",
  "Policy": "ArrayById",
  "IdField": "id",
  "ShardDirectories": [ "GAS/abilities" ],
  "AllowEmpty": true
}
```

The pipeline loads the main file first, then `*.json` files from shard directories in stable VFS order across Core and loaded Mods. If no file or shard is found, the loader throws unless the entry declares `AllowEmpty: true`.

### GAS extension keys

Mods register code handlers with mod-qualified keys:

```csharp
context.Extensions.Gas.RegisterBuiltinHandler("MyMod.ApplyHeat", ApplyHeat);
context.Extensions.Gas.RegisterGraphOp("MyMod.QueryThreat", GraphValueType.Float, QueryThreat);
```

The key must start with the loading mod id plus a dot. Other Mods may reference the provider key in their config, for example a graph node with `"op": "MyMod.QueryThreat"`, but they cannot register new handlers under `MyMod.*`.

Effect preset definitions resolve handler keys through `BuiltinHandlerRegistry`. Graph definitions resolve extension op keys through `GasGraphOpRegistry`. The compiled program then runs against an explicit `GasGraphOpHandlerTable`; there is no static singleton.

Extension graph ops may expose `Void`, `Bool`, `Int`, `Float`, or `Entity` outputs and may consume up to three `Bool`, `Int`, `Float`, or `Entity` inputs. `TargetList` is an implicit VM scratch structure, not a register type for Mod op signatures.

Preset type definitions remain data IR in `GAS/preset_types.json`. A Mod "codes a preset type" by registering the
C# phase handler or graph op it needs, then declaring a preset key that composes those handlers or graphs. This keeps
user variants out of Core enum space and keeps effect execution in the existing GAS phase pipeline.

### Performer extension keys

Performer commands and behaviors use the same startup registration model:

```csharp
context.Extensions.Presentation.RegisterPerformerCommand(
    "MyMod.SpawnRibbon",
    new PerformerCommandExtensionDescriptor(PerformerCommandRouteStrategy.SingleRuntime, SpawnRibbon));

context.Extensions.Presentation.RegisterPerformerBehavior(
    "MyMod.RibbonTick",
    new PerformerBehaviorExtensionDescriptor(PerformerBehaviorExecutionLane.ContinuousTick, TickRibbon));
```

Config and programmatic definitions must mark extension entries explicitly:

- command: `PerformerCommandKind.Extension`, extension `CommandKindId`, explicit `RouteStrategy`
- behavior: `BehaviorKind.Extension`, extension `KindId`, explicit `ExtensionLane`

Builtin commands and behaviors cannot carry mod ids or extension lane metadata.

## Scenarios

### Split an ability file

A mod may add `assets/Configs/GAS/abilities/fireball.json` after `GAS/abilities.json` declares `ShardDirectories: [ "GAS/abilities" ]`. The loader merges that shard into the same `GAS/abilities.json` logical config.

### Reuse another mod's graph op

`ProviderMod` registers `ProviderMod.QueryThreat`. `ConsumerMod` writes a graph node using `"op": "ProviderMod.QueryThreat"`. This is valid because registration ownership and config consumption are separate.

### Add a performer behavior

`WeatherMod` registers `WeatherMod.CloudDrift` as a continuous tick behavior. Its performer definition uses `BehaviorKind.Extension` and the resolved `KindId`. The behavior executes through the Performer behavior system, not a parallel presentation pipeline.

## Boundaries

- Do not add Core enum values for user variants unless the behavior is a new Core concept.
- Do not expose `ModExtensionHub`, `GasGraphOpRegistry`, `BuiltinHandlerRegistry`, or `PresetTypeRegistry` through `CoreServiceKeys`.
- Do not use `GasGraphOpHandlerTable.Instance`; runtime code must receive the table explicitly.
- Do not parse builtin handler names through enum fallback outside the legacy parser definition.
- Do not bypass `config_catalog.json` for formal feature configs.

## UAT

```gherkin
Feature: Split config and mod runtime extensions

  Scenario: A mod contributes an ability shard
    Given the catalog entry for "GAS/abilities.json" declares shard directory "GAS/abilities"
    And a mod contains "assets/Configs/GAS/abilities/fireball.json"
    When the game starts
    Then the fireball ability is loaded as part of "GAS/abilities.json"

  Scenario: A consumer mod reuses a provider graph op
    Given ProviderMod registers graph op "ProviderMod.QueryThreat"
    And ConsumerMod has a graph node using op "ProviderMod.QueryThreat"
    When graph configs compile
    Then the ConsumerMod graph compiles with ProviderMod's op code

  Scenario: A mod cannot register another mod's extension key
    Given ConsumerMod is loading
    When it tries to register "ProviderMod.OtherOp"
    Then startup fails with a namespace ownership error

  Scenario: A performer extension omits its route
    Given a performer rule uses an extension command
    And the command does not declare a route strategy
    When performer definitions compile
    Then startup fails before gameplay begins
```
