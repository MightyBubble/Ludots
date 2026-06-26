# Routing To Mass Execution

Backlinks: Epic #281, NAV-7 #290, NAV-2 #284, NAV-5 #287, NAV-6 #288.

## Background

Before NAV-7, the production route layer (`PathServiceRouter` / `AutoPathService`) and the production execution layer (`MassFlowSimulationState`) were independent. `pathing.json` could select Graph, Mesh, or Auto routing, but MassNavigation orders still reached execution through group targets and did not consume route waypoints. Small exact-route units and large flow-driven armies therefore could not share one map by profile.

## Goal

NAV-7 connects the route layer to MassFlow per-agent targets:

```text
MassNavigation order -> PathServiceRouter/AutoPathService -> waypoint cursor -> MassFlow per-agent target
```

Only agents whose `MassCrowdAgent.ProfileId` is declared by `Navigation/pathing.json` `agentTypes[].profileId` enter the route sink. Agents not declared in `pathing.json` keep the existing MassFlow group / flow execution. A declared profile that cannot solve or copy a route fails the order path loudly; it is not downgraded to a direct target.

In scope:

- Profile-gated route-to-execution sink for MassNavigation agents.
- Stateful waypoint cursor per `(orderToken, agentIndex)`.
- `PathResult.ResolvedDomain` so HUD/tests can see Graph vs Mesh selection.
- Strict service registration: `PathService`, `PathStore`, and `PathingConfig` must be present together.

Out of scope:

- Global replacement of `MoveToWorldCmOrderSystem`.
- Road-specific move-plan seam extraction; NAV-9 #303 owns the road showcase migration.

## User Story

As a player, I want small squads to follow exact road/navmesh routes while a large army still advances through MassFlow, so both movement styles can coexist on one map.

Given a map with both road/nodegraph and open terrain, when I command a routed squad profile and a flow-only army profile, then the routed squad consumes route waypoints while the flow-only army keeps its existing MassFlow target behavior.

As a designer, I want `pathing.json` to select `AutoCheapest`, `PreferGraph`, or `PreferMesh` per profile, so routing behavior changes without code edits.

Given a profile declared in `Navigation/pathing.json`, when I change its `selection.mode`, then the route sink receives the selected route domain through `PathResult.ResolvedDomain` and drives the agent through that route's waypoint list.

## UAT Showcase

Command:

```powershell
.\scripts\run-mod-launcher.cmd cli launch nav_route --adapter raylib
```

| Operation | Visible feedback |
|---|---|
| Start the route preset with road/nodegraph, navmesh, a small squad, and a large army | HUD lists each profile's `selection.mode` and the resolved route domain |
| Command the small squad profile configured as `PreferGraph` | The squad follows the road/nodegraph path; the path overlay and HUD show Graph |
| Command the large army profile that is not declared in `pathing.json` | The army keeps MassFlow group/flow execution and does not request per-agent paths |
| Change the squad profile to `PreferMesh` and resend the command | The squad follows the navmesh route; the HUD route domain changes to Mesh |
| Break the declared profile route data and resend the command | The command fails visibly/logs the route failure instead of moving directly |

## Configuration

Route gating is owned by `Navigation/pathing.json`:

```json
{
  "agentTypes": [
    {
      "id": "road_squad",
      "profileId": "light",
      "selection": {
        "mode": "PreferGraph"
      }
    }
  ]
}
```

Fields:

| Field | Owner | Meaning |
|---|---|---|
| `agentTypes[].id` | Pathing | Route-agent type passed to `PathRequest.AgentTypeId` |
| `agentTypes[].profileId` | AgentProfile / MassCrowd profile id | Enables route sink for MassCrowd agents with the same profile id |
| `selection.mode` | Pathing | Strict enum: `AutoCheapest`, `PreferGraph`, `PreferMesh` |
| `selection.graphBias`, `meshBias`, `graphCostWeight`, `meshCostWeight` | Pathing | Cost controls for `AutoCheapest` |

`MassNavigationConfig.agentProfiles` still owns movement speed and runtime profile distribution. Speed is not part of the unified AgentProfile registry.

## Config To Behavior Tests

The contract tests are in `src/Tests/PresentationTests/MassNavigationRouteExecutionContractTests.cs`.

They pin:

- A declared profile receives the first route waypoint as a MassFlow per-agent world target.
- An undeclared profile is ignored by the route sink and keeps existing MassFlow execution.
- The route cursor advances waypoints without solving every frame.
- A declared profile path failure returns `SolveFailed` and does not write a direct target.

Run:

```powershell
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter "MassNavigationRouteExecutionContractTests" /m:1 /nr:false
```

## Merge And Reuse

NAV-7 reuses:

- PR #235 Core-owned `MassCrowd` runtime.
- NAV-6 per-agent target API and arrival support.
- Existing `PathServiceRouter`, `AutoPathService`, `PathStore`, `PathingConfig`, and `AgentProfileRegistry`.

No additional branch is merged for NAV-7.

## Definition Of Done

- Route-to-MassFlow execution path exists for declared MassCrowd profiles.
- Flow-only profiles are not forced through per-agent route solving.
- Declared route failures are fail-fast, with no direct-target fallback.
- Resolved route domain is exposed in `PathResult`.
- Contract tests cover routing behavior and failure mode.
- GitBook reference is updated and linked from the reference index.
