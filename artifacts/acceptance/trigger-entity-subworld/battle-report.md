# Entity TriggerGraph 聚落作用域验收报告

Status: BLOCKED - target test execution not completed
Date: 2026-08-24

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

## Verification attempt

Command: `dotnet build src/Tests/GasTests/GasTests.csproj --no-restore`

Result: blocked because this clean worktree has no generated `project.assets.json`. A restore attempt also hit the repository's known worktree NuGet `path1` failure in unrelated project references. No test pass is claimed.

## Required next evidence

Run the three added tests in `TriggerGraphEntityDomainTests` from an environment with the repository dependency assets available, then replace this report with the actual NUnit result, battle trace, and runtime Agent Bridge evidence.
