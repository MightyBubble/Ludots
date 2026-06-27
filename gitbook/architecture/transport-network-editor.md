# In-session Transport Network Editor

Parent epic: [#462](https://github.com/MightyBubble/Ludots/issues/462). Boundary ADR: `docs/adr/ADR-0004-transport-network-editor-boundary.md`.

The transport editor is the NodeGraph authoring leg of the Live Map Editor. It edits the same `TransportNetworkAsset` that runtime mods load, then derives graph chunks and ribbon chunks through the existing Core baker.

## Ownership

| Concern | SSOT | Editor behavior |
|---|---|---|
| Authoring asset | `TransportNetworkAsset` loaded from `TransportNetwork/transport_network.json` | Node, segment, area/tag, capacity, and flow commands mutate the live asset reference |
| Validation | `TransportNetworkAsset.Validate()` | Every edit validates before derived outputs are refreshed |
| Bake | `TransportNetworkBaker.Bake(asset, chunkSizeCm)` | Whole-asset bake is the correctness baseline |
| Graph runtime | `ChunkedNodeGraphStore` / `LoadedGraphRuntime` | Old editor-derived graph source is disposed, old chunks are cleared, new baked chunks load through the store |
| Ribbon runtime | `TransportNetworkRibbonSource` / `SurfaceSourcePayloadRegistry` | Runtime and editor both use `TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId`, so the live editor replaces the same authoritative ribbon scope instead of drawing a second ribbon |
| Route validation | `GraphEdgeProjectionQuery`, `LoadedChunkSolvePrimer`, `PolylineGoalSnapQuery`, `PathServiceRouter` / `AutoPathService` | Picked start/goal are projected onto the loaded graph and solved by Core pathing |
| Save | `TransportNetwork/transport_network.json` plus `config_catalog.json` | Save validates, writes, ensures catalog registration, reloads through `TransportNetworkAssetLoader`, and re-bakes |
| Cost | `Navigation/pathing.json`, `AgentProfile`, optional `GraphEdgeCostOverlay` | The editor does not edit or bake edge costs |

## Tool Modes

| Mode | Viewport input | Panel commands | Result |
|---|---|---|---|
| `node` | Left click selects a nearby node, or creates one when none is near; right click moves the selected node | Add, Select, Update, Move, Delete, Kind, Tags | `TransportNetworkAsset.nodes` changes, selected-node id changes rewrite segment node references atomically, then Core bake refreshes |
| `segment` | Left click appends a free draft point; right click commits the draft with current segment defaults | Begin, Point, Snap Point, Undo, Commit, Select, Update, Insert Pt, Move Pt, Del Pt, Delete | `TransportNetworkAsset.segments` changes, then Core bake refreshes |
| `route` | Left click sets start; right click sets goal and queries | Agent select, Requery Route | Core pathing projects endpoints, solves, and Raylib draws the route overlay; changing Agent immediately updates the runtime route profile |

The colored node/segment overlay is an editor gizmo for authoring feedback. It is not a replacement for baked ribbon rendering. The baked ribbon path remains:

```text
TransportNetworkBaker -> TransportNetworkRibbonSource -> SurfaceSourcePayloadRegistry -> Raylib/Core presentation
```

Runtime showcase code and live-editor code must use the same Core ribbon scope composer. A different editor-owned scope id would render old and newly baked transport ribbons side by side, which violates the single-derived-output rule.

## Save Contract

`saveMap` remains the topbar save command. For maps without a loaded transport asset it saves the #451 map/terrain/entity/nav assets only. For maps with a loaded transport asset it also calls the transport save path:

1. `TransportNetworkAsset.Validate()`
2. write `assets/TransportNetwork/transport_network.json` or the existing exact transport asset source
3. ensure `{ "Path": "TransportNetwork/transport_network.json", "Policy": "Replace" }` in `assets/Configs/config_catalog.json`
4. reload through `TransportNetworkAssetLoader`
5. compare normalized asset semantics and re-bake

Multiple writable transport asset sources fail fast.

## UAT Checklist

| Step | Command or operation | Expected feedback |
|---|---|---|
| 1 | Launch `preset:live_map_editor_transport_network_cef_raylib` | Transport panel shows asset id, nodes, segments, baked graph/ribbon chunk counts |
| 2 | Select Transport -> Node, left click empty ground | New node appears in the Raylib gizmo overlay; DataPlane node count increments |
| 3 | Set Kind=`Ford`, tags as needed, then Update/move/delete/select nodes | Asset validation accepts legal edits, node renames keep segment references intact, and referenced-node deletion is rejected |
| 4 | Select Transport -> Segment, Begin, add points, set area/tag/flow/depth/width, Commit; then Select and Insert Pt | Segment appears in the authoring overlay and the bake message reports refreshed graph/ribbon chunks |
| 5 | Press Bake | `TransportNetworkBaker.Bake(asset, chunkSizeCm)` runs and graph/ribbon derived outputs refresh |
| 6 | Select Transport -> Route, choose agent type, left click start, right click goal | Route result reports status, point count, elapsed microseconds, and Raylib draws the route overlay |
| 7 | Press Save | `transport_network.json` is written and reloads through the strict loader |
| 8 | Relaunch the same mod stack | Nodes, segments, bake output, and route behavior match the saved state |

`CapabilityStandardTransportNetworkMod` provides three data-driven route profiles for this checklist:

| Agent type | What it proves |
|---|---|
| `Transport.FootScout` | Tag policy can keep foot traffic on crossing/land transport edges and off water edges |
| `Transport.ShallowBoat` | A small craft can use shallow water and pays the configured upstream cost |
| `Transport.DeepDraftShip` | `AgentProfile.draftCm` / `beamCm` capacity gates block shallow or narrow segments and force deep-water routing |

## Non-goals

- No JavaScript world renderer for transport geometry.
- No private graph or direct `.graph` authoring.
- No transport-cost editing in the asset.
- No lane/traffic simulation authoring in this epic.
