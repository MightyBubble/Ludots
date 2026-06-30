# Move Planning MassNavigationFlow Road Execution

Parent: [Epic #281](https://github.com/MightyBubble/Ludots/issues/281). This page lands [NAV-9 #303](https://github.com/MightyBubble/Ludots/issues/303) and must be read before [NAV-8 #289](https://github.com/MightyBubble/Ludots/issues/289) deletes the deprecated point-target execution stack.

## Background

Before NAV-9, `RoadNetworkShowcaseMod` owned both road-specific route policy and generic move-plan runtime seams:

| Old owner | Responsibility | Problem |
|---|---|---|
| `RoadNavPlanStore` | Stores active route points | Generic plan storage was tied to one showcase |
| `RoadMoveRuntimeService` | Binds active orders to route runtime | Other mods would need to copy `Road*` classes |
| `RoadRouteWalkStrategy` | Writes execution target | It drove retired execution components |
| `OrderArgs.Spatial.A0` | Runtime waypoint cursor in older paths | Authored order payload was polluted by execution state |

This blocked NAV-8 because the already-merged road showcase was a real downstream consumer of the retired execution stack.

## Goal

The generic seam now lives in Core:

| Core type | Owner | Contract |
|---|---|---|
| `MovePlanStore` | `Ludots.Core.MovePlanning` | Stores copied move waypoints per entity/order |
| `MovePlanRuntimeService` | `Ludots.Core.MovePlanning` | Binds active orders and owns lifecycle/runtime components |
| `MovePlanOrderRuntime` | `Ludots.Core.MovePlanning` | Tracks active order id, lifecycle, timeout count, execution generation |
| `MovePlanRuntime` | `Ludots.Core.MovePlanning` | Tracks plan generation, final goal, and `CurrentWaypointIndex` |
| `MovePlanExecutionIntent` | `Ludots.Core.MovePlanning` | Publishes one sampled target to an execution sink |
| `IMovePlanExecutionSink` | `Ludots.Core.MovePlanning` | Generic sink interface |
| `MassNavigationMovePlanExecutionSink` | `Ludots.Core.MassNavigation.Runtime` | Applies intents to `MassNavigationSimulationRuntime` per-agent targets |

Road-specific policy stays in the showcase:

| Road type | Owner | Why it stays road-specific |
|---|---|---|
| `RoadRouteQueryService` / `RoadRoutePlanningService` | `RoadNetworkShowcaseMod` | Road graph and corridor preference policy |
| `RoadRouteProfileCatalog` | `RoadNetworkShowcaseMod` | Showcase preset tuning |
| `RoadRouteSelectionStrategy` | `RoadNetworkShowcaseMod` | Route waypoint selection and road arrival rules |
| `RoadRoutePreviewSplineBuilder` | `RoadNetworkShowcaseMod` | Road UI preview |
| Fort capture and road AI systems | `RoadNetworkShowcaseMod` | Showcase gameplay |

Execution now flows as:

```text
right-click order
  -> RoadMoveOrderExpander computes road follow order
  -> MovePlanStore copies route points
  -> RoadMovePlanSelectionSystem samples the next road target
  -> MovePlanExecutionIntent
  -> MassNavigationMovePlanExecutionSink
  -> MassNavigationSimulationRuntime / MassNavigationFlow
```

There is no retired component execution sink in the road showcase.

## User Story

As a showcase player, I can box-select road columns and right-click a distant road destination, so the units follow road corridors with the same visible behavior as before.

As a gameplay developer, I can reuse `Ludots.Core.MovePlanning` for another order-to-plan-to-execution feature without copying road-named classes.

As a reviewer, I can verify that road execution uses MassNavigationFlow per-agent targets and that `OrderArgs.Spatial.A0` is not used as a runtime cursor.

## UAT Showcase

Run:

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --filter "RoadNetworkShowcase_PlayableInitialDragSelectRightClick_StartsRoadMoveWithoutReset|RoadNetworkShowcase_PlayableInitialMultiSelectionRightClick_StartsRoadMoveWithoutReset" /m:1 /nr:false --no-restore
```

| Operation | Visible/test feedback |
|---|---|
| Load `RoadNetworkShowcaseMod` with `road_network_showcase_chunked` | Tactical camera opens on Blue Vanguard and map participant binding sets local player 1 |
| Drag-select Blue Vanguard, Blue North Column, and Blue South Column | Formal selection count becomes 3 through `CurrentSelectionApplySystem` |
| Right-click a visible ground point | Road orders are submitted through the real input/order bridge |
| Let fixed steps run | Selected columns receive MassNavigationFlow per-agent navigation targets |
| Inspect road entities | No retired execution components are required |

The showcase uses the real nav bake/pathing, input bridge, selection runtime, road planning, and MassNavigationFlow execution. It is not a script stub.

## Configuration

Road map authoring must bind the local participant through the formal map schema:

```json
{
  "Teams": [
    { "TeamId": 1, "RepresentativeInstanceId": "road.team.blue" },
    { "TeamId": 2, "RepresentativeInstanceId": "road.team.red" }
  ],
  "Players": [
    { "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "road.player.blue" }
  ]
}
```

Road-specific MassNavigationFlow tuning remains in `RoadNetworkShowcaseMod/assets/MassNavigationConfig.json`. The map uses `RoadNetwork.Camera.Tactical` and road-specific layers from `RoadNetworkShowcaseLayerNames`.

## Configuration To Behavior

| Configuration or code fact | Behavior | Contract test |
|---|---|---|
| `game.json.startupSelectedPlayerId = 1` for `road.player.blue` | `LoadStartupMap()` injects launch context so `LocalPlayerEntityResolverSystem` keeps a valid selection owner before the confirm press frame | `RoadNetworkShowcase_PlayableInitialDragSelectRightClick_StartsRoadMoveWithoutReset` |
| Road execution source contains no retired execution component references | Road movement is not coupled to the deprecated sink | `RoadNetworkShowcaseMovePlanExecution_DoesNotReferenceLegacyExecutionComponents` |
| Core `MovePlanning` source contains no road/corridor/fort names | Core seam remains reusable outside the road showcase | `CoreMovePlanningSeam_DoesNotReferenceRoadShowcasePolicy` |
| Road execution source contains no `.A0` cursor usage | Runtime waypoint cursor lives in `MovePlanRuntime.CurrentWaypointIndex` | `RoadMovePlanRuntimeCursor_DoesNotUseOrderSpatialA0` |
| Entity lacks `MassNavigationAgentIndex` | Mass sink returns false and does not invent an execution binding | `MassNavigationMovePlanExecutionSink_RequiresMassNavigationAgentIndex` |
| MassNavigationFlow reports an unchanged held target | Sink treats the maintained target as successful | `MassNavigationMovePlanExecutionSink_UnchangedHeldTarget_ReturnsSuccess` |

## Merge And Reuse

NAV-9 reuses:

| Reused item | Purpose |
|---|---|
| PR #235 Core-owned `MassNavigation` runtime | MassNavigationFlow execution owner |
| NAV-6 per-agent target API | `MassNavigationMovePlanExecutionSink` target writes |
| NAV-7 route-to-execution concepts | Separate route calculation from MassNavigationFlow execution |
| `ParticipantBindingResolver` | Formal local player / player lookup binding for road selection |
| `OrderBuffer.RuntimeInt0` | Generic move-to cursor outside road move plans |

No PR is merged during NAV-9 itself. It is built on top of the already-merged PR #235 work in this branch.

## DoD

- `src/Core/MovePlanning` contains generic move-plan storage/runtime interfaces with no road-specific naming.
- Road route planning, corridor preferences, preview splines, fort capture, and road AI remain showcase-owned.
- Road execution writes MassNavigationFlow per-agent targets through `MassNavigationMovePlanExecutionSink`.
- Road showcase source has zero retired execution component references.
- Road move-plan cursor uses `MovePlanRuntime.CurrentWaypointIndex`, not `OrderArgs.Spatial.A0`.
- Contract tests cover source boundaries, sink failure/success cases, runtime cursor behavior, and playable road selection/right-click UAT.
- GitBook links back to #281 and #303.
