# Transport Network SSOT

Parent: [Epic #415](https://github.com/MightyBubble/Ludots/issues/415). This page is the architecture SSOT for the NodeGraph authoring-to-graph production chain.

## Goal

`TransportNetworkAsset` is the single authored geometry source for NodeGraph transport topology. The same asset produces both:

- `GraphChunkData` chunks for routing through `ChunkedNodeGraphStore` / `LoadedGraphRuntime`.
- `SurfaceSplineSegment` ribbon payloads for presentation through `SurfaceSourcePayloadRegistry`.

The chain is one-way:

```text
TransportNetwork/transport_network.json
  -> TransportNetworkAssetLoader
  -> TransportNetworkBaker
  -> TransportNetworkBakedAsset
      -> GraphChunkData chunks
      -> SurfaceSplineSegment ribbon chunks
```

No mod should author `.graph` as an independent source when this transport asset exists. The derived graph and ribbon must not drift.

## Reuse List

Implementations must reuse the existing infrastructure:

| Capability | Owner |
|---|---|
| Config loading and merge policy | `ConfigPipeline`, `ConfigCatalog` |
| Chunked NodeGraph storage | `ChunkedNodeGraphStore`, `GraphChunkData`, `GraphCrossEdge` |
| Runtime graph view | `LoadedGraphRuntime`, `WorldGridLoadedChunks` |
| Routing | `PathServiceRouter`, `AutoPathService`, `PathStore` |
| Graph traversal semantics | `TagRuleTraversalPolicy`, `GraphEdgeCostOverlay` |
| Agent geometry | `AgentProfileRegistry` from `Navigation/agent_profiles.json` |
| Presentation source payloads | `SurfaceSourcePayloadRegistry`, `SurfaceSplineSegment` |

Do not add a second config loader, a second route stack, a private presentation lane, or a topology-specific fallback.

## Asset Authority

The authored file is:

```json
{ "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }
```

`TransportNetworkAssetLoader` loads it through `ConfigPipeline`, requires the catalog entry, uses strict camelCase JSON, and reads enum values as strings. Unknown casing, missing explicit fields, unknown node references, duplicate node ids, or invalid numeric ranges fail during load/validation.

## Bake Contract

`TransportNetworkBaker.Bake(asset, chunkSizeCm)` validates the asset and deterministically samples each segment into chunked graph data. It also derives ribbon chunks from the same segment geometry.

The graph output carries:

- node positions in world centimeters;
- node tags, including non-normal node kind tags such as `Transport.NodeKind.Port`;
- edge tags from `segments[].tags`;
- edge area tag from `segments[].areaId`;
- flow tags `Transport.Flow.Downstream` / `Transport.Flow.Upstream` when `flowDirection` is set;
- edge capacity fields `depthCm` and `widthCm`;
- derived geometric base length for the graph edge.

The baker does not author per-agent route policy. Per-agent traversal and dynamic cost stay in `Navigation/pathing.json`, `AgentProfileRegistry`, and `GraphEdgeCostOverlay`.

## Routing Contract

`AutoPathService` remains the production route owner. For NodeGraph traversal it compiles:

- `nodeGraph.requiredTagsAll` and `nodeGraph.forbiddenTagsAny` into a tag filter;
- `nodeGraph.tagCostRules[]` into static tag rules;
- `AgentProfileConfig.draftCm` and `beamCm` into capacity filters;
- `nodeGraph.useDynamicOverlay` into optional `GraphEdgeCostOverlay` reads.

When `useDynamicOverlay` is `true`, a missing `GraphEdgeCostOverlay` is an error. The overlay formula is:

```text
finalCost = staticCost * (1 + overlayCostMul) + overlayCostAdd
```

An overlay blocked edge is not traversable.

## Query Contract

Reusable graph query helpers live in `Ludots.Core.Navigation.GraphQuery` and must stay domain-neutral:

| Service | Use |
|---|---|
| `GraphEdgeProjectionQuery` | project a world point onto the nearest outgoing graph edge |
| `PolylineGoalSnapQuery` | trim a route polyline to the point nearest a goal |
| `GraphHybridRouteBuilder` | stitch multiple path legs through the existing path service |
| `LoadedChunkSolvePrimer` | prime loaded chunks for a solve bound |

Road, rail, water, province, and lane-specific policy stays in mods or config. Core GraphQuery names must not contain business terms such as road, corridor, fort, or landmark.

## Capability Standard Root

The formal acceptance root is:

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_transport_network' --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_transport_network_raylib'
```

The root mod only demonstrates authoring, bake, route, and ribbon derivation for the transport capability. Business gameplay such as capture points, AI scoring, or scenario-specific route bias belongs outside this root.

## DoD

Transport Network SSOT is complete when:

- transport topology is authored from `TransportNetwork/transport_network.json`;
- graph chunks and ribbons derive from the same asset;
- NodeGraph edges preserve semantic tags and capacity through chunk flattening;
- route behavior is driven by `Navigation/pathing.json`, `AgentProfileRegistry`, and optional dynamic overlay;
- capability-standard launch binding and preset resolve to the transport root;
- Core TransportNetwork and GraphQuery code stays domain-neutral;
- focused tests cover strict config, deterministic bake, tag/capacity propagation, overlay behavior, water capacity, graph query helpers, and launcher contract.
