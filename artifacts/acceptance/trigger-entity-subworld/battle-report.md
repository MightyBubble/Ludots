# Entity TriggerGraph 聚落作用域验收报告

Status: PASS - targeted TriggerGraph regression suite completed
Date: 2026-08-26

## Scenario

- Entity template declares multiple TriggerGraphs.
- Root template carries `EntityTriggerGraphAggregateRoot`.
- A runtime child is attached with `AttachmentOps.Attach`.
- A source event from the child must update the root graph state; an external entity must not.

## Implementation evidence

- `src/Core/Gameplay/MapTriggers/EntityTriggerGraphMounts.cs`
- `src/Core/Gameplay/MapTriggers/TriggerGraphMountTrigger.cs`
- `src/Core/Systems/MapLoader.cs`
- `src/Core/Config/ComponentRegistry.cs`
- `src/Tests/GasTests/Graph/TriggerGraphEntityDomainTests.cs`

## Verification

Command: `dotnet test src/Tests/GasTests/GasTests.csproj --no-restore --filter "FullyQualifiedName~AbilityExecLoaderFailFastTests|FullyQualifiedName~TriggerGraphMountTests|FullyQualifiedName~TriggerGraphEntityDomainTests"`

Result: 77/77 passed on the rebased branch. The suite covers entity aggregate scope, ordered multi-graph mounts, ability allowlists, ModId isolation, global route registration, and fixed-step Mod graph resumption.

Runtime AgentBridge evidence is intentionally not claimed here; the showcase remains pending the separate runtime gate.
