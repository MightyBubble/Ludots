> Update required / superseded selection boundary: formal SelectionRuntime is retired.
> WebUI topics and commands must use EntityCollectionStore and `collection.command.source` for
> current command actors, with no fallback to SelectionRuntime.
# WebUI DataPlane Architecture

This document defines the higher-level WebUI DataPlane that sits above the browser surface runtime. It is the contract shape for feeding Ludots-owned runtime data into WebUI panels without turning browser engines, UE5 adapters, or web applications into Core data owners.

## 1 Scope

The WebUI DataPlane belongs to `Ludots.WebUI`.

It is a high-level Mod-facing data and event layer. It does not own browser pixels, native windows, engine processes, packaged web resources, or platform texture upload. Those remain under `Ludots.UI.Browser` and concrete transport adapters.

The intended layering is:

```text
Mod / gameplay systems
    -> Ludots.WebUI DataPlane topics and event facade
        -> EntityCollectionStore / presentation buffers / other existing Core stores
            -> transport adapter
                -> Ludots-started CEF surface or external UE5 BLUI surface
                    -> Web app
```

`Ludots.UI.Browser` remains the lower-level browser surface contract. `Ludots.WebUI` remains the higher-level event/API facade and owns the WebUI DataPlane vocabulary.

## 2 Reused Infrastructure

The DataPlane must reuse existing infrastructure before adding new stores:

| Need | Existing infrastructure | Rule |
|------|-------------------------|------|
| Entity sets, list windows, inspectors, command-source views | `EntityCollectionStore` | Use owner + collection key addressing, revision checks, and span/window reads. Do not create a parallel WebUI entity-list store. |
| High-frequency marker/entity topics | `MinimapMarkerBuffer` and `MinimapScreenMarkerBuffer` design | Use SoA arrays, explicit capacity, bucket keys for render/data grouping, and drop diagnostics. Do not allocate per entity or per marker on the hot path. |
| Browser transport | `IBrowserSurface.Messages` / `IBrowserMessageBridge` | Use the browser bridge as one transport implementation, not as the DataPlane owner. |
| Mod event API | `IWebUIBridge`, `IWebUIBridgeFactory`, `ModWebEventScope` | Keep Mod-facing lifecycle and event registration in `Ludots.WebUI`. |

Missing services or unknown topic/query ids must fail explicitly at the consuming boundary. Silent fallback to selection, current panel state, adapter-local cache, or browser-side state is forbidden.
Browser adapters must not become selection truth; user-facing selection remains a derived label for explicit `EntityCollectionStore` topics such as `collection.command.source`.
Panel Kit manifests validate topic existence through `WebUiDataPlaneRuntime.IsTopicRegistered` at load time; unknown topic ids fail with the concrete id in the exception message.
Resource attribute panels (WPK-2) publish `owner` / `descriptor` / `revision` / `values` snapshots via `WebUiResourceAttributeTopicProducer`; see [WebUI Resource Attribute Panel](webui_resource_attribute_panel.md).
Production / Worker / Queue overview topics (WPK-4) project existing EntityCommandPanel status/queue, OrderBuffer, and entity-collection worker buckets; see [webui_production_overview_panel.md](webui_production_overview_panel.md).
Tooltip panels (WPK-5) publish structured rich-text snapshots via `WebUiTooltipTopicProducer`; see [WebUI Tooltip + Rich Text](webui_tooltip_rich_text.md).
Notification panels (WPK-7) publish ordered message snapshots via `NotificationWebUiTopicProducer` from an independent `NotificationRuntime`; see [WebUI Notification Panel](webui_notification_panel.md).
TechTree / Progression panels (WPK-9) publish `scopeHost` / `actor` / `descriptor` / `revision` / `nodes` snapshots via `WebUiTechTreeTopicProducer`; see [WebUI TechTree / Progression Panel](webui_techtree_progression_panel.md).

## 3 Entity Collection Topics

Entity collection topics are WebUI projections of `EntityCollectionStore`.

They are addressed by:

- owner entity
- collection key
- optional window request: start row and row count
- observed revision

The DataPlane reads collection metadata through `TryGet(...)` / `TryGetView(...)` and rows through `CopyWindow(...)`, `CopyEntities(...)`, `TryGetEntityAt(...)`, or `TryGetRowAt(...)`.

Required behavior:

- Use `EntityCollectionView.Revision` as the change token for web-side cache invalidation.
- Send windows instead of whole collections when panels only display a visible range.
- Preserve `EntityCollectionSourceKind` and `EntityCollectionRoleKind` as descriptors in the payload.
- Treat user-facing selection as shorthand for explicit entity collection topics; current command actors are `collection.command.source`, with no `SelectionRuntime` fallback.
- Use explicit collection keys for WebUI panels. Unknown keys are errors.

This lets WebUI panels reuse the same collection truth used by EntityInfo, command panels, acquisition previews, debug views, spatial query results, and GAS graph results.

## 4 High-Frequency Marker And Entity Topics

High-frequency topics include minimap markers, world-space entity markers, screen-space markers, tactical overlays, and dense WebUI visual feeds.

Their model should follow `MinimapMarkerBuffer` and `MinimapScreenMarkerBuffer`:

- Store fields as SoA arrays instead of arrays of objects.
- Allocate capacity explicitly and report `DroppedSinceClear` / `DroppedTotal` style diagnostics.
- Begin each frame with a clear frame boundary.
- Use stable ids so the web side can diff without treating every row as new.
- Use style or data bucket keys when many rows share render/data shape.
- Stage and materialize bucketed data when grouping reduces downstream transport or rendering work.
- Quantize high-cardinality values, such as orientation, into bounded buckets when exact precision is not needed by the panel.

The bucket concept is part of the DataPlane shape, not a minimap-only trick. A WebUI topic for entity markers can group by icon/style/status bucket before transport, just as `MinimapScreenMarkerBuffer` groups screen markers by `MinimapMarkerRenderBucketKey`.

Drop diagnostics are contract data. A WebUI panel that receives a sampled or dropped feed must be able to show or log that the topic exceeded its configured budget. Adapters must not hide dropped rows by silently requesting unbounded payloads.

## 5 Transport Paths

The DataPlane is transport-neutral. `IWebUiDataTransport` is the semantic transport boundary. It carries DataPlane topics, delivery semantics, command acknowledgements, transport capabilities, diagnostics, and shared-buffer descriptors. It does not expose CEF, V8, BLUI, UE, Raylib, platform texture, or browser engine handles.

Every Web app uses the same browser-facing facade:

```text
React / Web app
    -> window.ludotsDataplane
        -> Browser Native Bridge
            -> IWebUiDataTransport
                -> WebUiDataPlaneRuntime
```

`window.ludotsDataplane` is the only production JS entrypoint. A CEF provider may install that facade from renderer/V8-side script injection, and a UE5 BLUI adapter may install the same facade over its private BLUI message API. The Web app must not call `window.CefSharp`, BLUI-private globals, or provider-specific objects directly.

Control and data are separate lanes:

- The control lane carries handshake, subscribe, unsubscribe, command, ack/error, diagnostics, and shared-buffer descriptor messages.
- The data lane carries high-volume binary payloads. A message transport may carry base64 chunks as a low-capability mode; a shared-memory transport carries a shared-buffer descriptor plus sequence/tick/drop/coalesce counters.
- A shared-buffer descriptor identifies buffer id, topic, schema id, layout, capacity, header bytes, byte range, sequence, tick, dropped packets, and coalesced packets. The descriptor is DataPlane contract data; native handles and OS mapping details stay inside the concrete provider.
- The Windows CEF/Raylib slice uses `BrowserSharedMemoryBufferStore` to allocate host-owned named memory-mapped files and registers only buffer-id readers with `BrowserSharedBufferBridge`. Descriptors sent to Web apps never include the OS memory-map name.
- The CEF provider exposes `window.ludotsDataplane.readSharedBuffer(descriptor)` through a provider-local native bridge. Web apps still call the standard facade and never call CEF/CefSharp objects directly.

No implicit fallback is allowed. Browser hosts must explicitly negotiate capabilities during handshake. If a Web app requires `shared-memory` or `shared-buffer-descriptor` and the host only has message/base64 transport, the session returns a transport capability mismatch error instead of silently downgrading to a slower path.

The DataPlane can flow through two supported browser paths:

| Path | Owner | Role |
|------|-------|------|
| Ludots-started CEF | Ludots process / CEF provider adapter | Built-in compatibility path for arbitrary Web apps launched by Ludots through `Ludots.UI.Browser.Cef`. |
| UE5 BLUI | External UE5 adapter | Commercial engine transport adapter path. It hosts BLUI/CEF and forwards WebUI DataPlane messages, but it does not enter Core and does not own DataPlane semantics. |

Both paths consume the same high-level WebUI topics and event names. The difference is only the transport binding and browser lifecycle owner.

UE5 BLUI is therefore a reference transport shape, not a Core dependency:

- it may translate DataPlane messages into BLUI/CEF browser process messages;
- it may expose a shared-buffer descriptor through its own Browser Native Bridge;
- it may translate Web events back into `IWebUIBridge`;
- it may own UE textures, widgets, and native lifecycle;
- it must not define new Core contracts, collection semantics, or marker buffer semantics.

## 6 Benchmark Baseline

WebUI DataPlane performance is measured under `artifacts/benchmarks/webui-dataplane`.

The benchmark baseline must stay separate from React Flow, browser animation, browser surface upload, and showcase-specific business logic. It records transport mode, publish CPU, managed allocations, packet count, payload bytes, expected managed copy count, dropped packets, and coalesced packets. Browser frame time, command RTT, and input latency are nullable fields until a host/browser runner supplies those measurements.

Required benchmark scenarios are:

- `surface-idle`: browser surface composition/upload cost only.
- `input-latency`: Ludots input timestamp to Web handler timestamp.
- `command-rtt`: Web command to C# ack/error round trip.
- `entity-10k-delta`: high-frequency RTS entity delta.
- `entity-100k-snapshot`: low-frequency full entity snapshot.
- `minimap-250k-static`: 4X map marker snapshot.
- `mixed-rts`: input, command, entity delta, and minimap delta together.

The first checked-in harness establishes machine-readable baseline rows; regression gates compare the same preset on the same machine and must document material threshold changes.

## 7 Browser-Host Conformance Checklist

An adapter that hosts a browser outside Ludots, including UE5 BLUI, must pass this checklist before claiming WebUI DataPlane support:

- Install `window.ludotsDataplane` before the app sends handshake.
- Implement `postMessage`, `addEventListener`, and `removeEventListener` with the same facade shape as Ludots-started CEF.
- If the adapter invokes Ludots-owned CEF through a `browserRuntime.providerAssemblyPath`, register dependency resolution from that provider assembly's `.deps.json` before loading the provider. Do not hardcode CefSharp dependency names and do not resolve the provider through any Mod load plan.
- Return capability negotiation fields for message, binary, shared-memory, chunking, expected copy count, and delivery semantics.
- Fail fast on missing required capabilities; do not downgrade to message/base64 without an explicit mock or preview mode.
- Forward handshake, subscribe, snapshot, delta, command ack/error, diagnostics, and session detach.
- Route pointer, wheel, middle-button, keyboard focus, alpha hit-test, and passthrough decisions through Ludots input ownership, not through adapter-local gameplay rules.
- Keep BLUI/CEF/V8/UE object lifetimes and native handles inside the adapter.
- Never interpret Ludots topics, entity collections, minimap schemas, commands, or selection truth inside the browser host.

## 8 Boundary Rules

- `Ludots.WebUI` may depend on lower-level browser contracts to implement browser-backed transport.
- `Ludots.UI.Browser` must not depend on `Ludots.WebUI`.
- `Ludots.WebUI` must not duplicate `EntityCollectionStore`.
- `Ludots.WebUI` must not duplicate high-frequency marker buffers when existing SoA/bucket/drop-diagnostic infrastructure applies.
- Browser providers must not introduce new gameplay truth. They only carry messages, frames, input, resources, and lifecycle.
- UE5, BLUI, CEF native handles, platform windows, and texture objects stay inside adapter/provider assemblies.
- Ludots-owned CEF provider package loading belongs to the host adapter bootstrap. Mods may request or require browser runtime capability, but they must not locate, package, load, initialize, unload, or provide CEF.
- CEF renderer/V8 injection is provider implementation detail and must not enter Core, WebUI contracts, or DataPlane contracts.
- Browser-side caches are derived views. Their invalidation is driven by DataPlane revision, sequence, and diagnostics fields from Ludots.

## 9 Issue SSOT Anchors

Implementation issues should reference this page and ADR-0003 instead of redefining the boundary locally.

Use these issue slices when work is split:

- DataPlane vocabulary and Mod-facing API live in `Ludots.WebUI`.
- `IWebUiDataTransport`, capability negotiation, and shared-buffer descriptors are the transport SSOT.
- Collection topics are adapters over `EntityCollectionStore`.
- High-frequency marker/entity topics follow the minimap SoA/bucket/drop-diagnostic pattern.
- Ludots-started CEF and UE5 BLUI are separate transport adapters over the same DataPlane vocabulary.
- UE5 BLUI work must stay outside Core and must not introduce Core-facing UE or BLUI types.

## 10 Acceptance Evidence

Current evidence is architectural, source-aligned, and executable:

- Browser surface boundary: `docs/architecture/browser_ui_runtime.md`
- Browser ADR: `docs/adr/ADR-0003-browser-ui-runtime-contract.md`
- Entity collection store: `docs/architecture/entity_collection_query_infrastructure.md`
- Marker buffer model: `src/Core/Presentation/Minimap/MinimapMarkerBuffer.cs`
- Screen marker bucket model: `src/Core/Presentation/Minimap/MinimapScreenMarkerBuffer.cs`
- WebUI facade: `src/Libraries/Ludots.WebUI/`
- DataPlane transport contracts: `src/Libraries/Ludots.WebUI.DataPlane/`
- Panel Kit manifest (WPK-1 composition contract): `src/Libraries/Ludots.WebUI.PanelKit/` and `docs/architecture/webui_panel_kit_manifest.md`
- Tooltip + rich text (WPK-5): `src/Libraries/Ludots.WebUI.PanelKit/WebUiTooltip*` / `WebUiRichText*` and `docs/architecture/webui_tooltip_rich_text.md`
- Quest Objective panel projection (WPK-6): `QuestObjectiveWebUiTopicProducer` and `docs/architecture/webui_quest_objective_panel.md`
- Notification panel (WPK-7): `src/Libraries/Ludots.WebUI.DataPlane/Notification*` and `docs/architecture/webui_notification_panel.md`
- Shared-memory host transport: `src/Libraries/Ludots.WebUI.Browser/BrowserSharedMemoryDataTransport.cs`
- Host-owned MMF buffer store: `src/Libraries/Ludots.WebUI.Browser/BrowserSharedMemoryBufferStore.cs`
- Provider-neutral shared-buffer bridge: `src/Libraries/Ludots.UI.Browser/BrowserSharedBufferBridge.cs`
- CEF native shared-buffer facade: `src/Libraries/Ludots.UI.Browser.Cef/CefDataPlaneNativeBridge.cs`
- React Flow showcase shared-memory wiring: `mods/showcases/browser_react_flow/BrowserReactFlowShowcaseMod/BrowserReactFlowShowcaseModEntry.cs`
- Benchmark harness: `src/Tests/WebUiDataPlaneTests/WebUiDataPlaneBenchmarkTests.cs`

Executable evidence includes `BrowserSharedMemoryDataTransportTests`, which reopens the named memory-mapped file and verifies payload bytes, rejects stale descriptors after ring overwrite, and proves binary packets for unconfigured topics fail instead of falling back to base64. The benchmark harness records real `BrowserMessageBridgeDataTransport` base64 chunks next to real `BrowserSharedMemoryDataTransport` descriptor messages, with `observedBase64Chunks = 0` for shared memory.

Future implementation evidence should add focused tests around browser-host input latency, alpha passthrough diagnostics, and transport parity between Ludots-started CEF and UE5 BLUI adapters.
