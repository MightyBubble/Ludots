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
- Keep formal selection owned by `SelectionRuntime`; collection topics can display selection views but must not become selection truth.
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

The DataPlane is transport-neutral. It can flow through two supported browser paths:

| Path | Owner | Role |
|------|-------|------|
| Ludots-started CEF | Ludots process / CEF provider adapter | Built-in compatibility path for arbitrary Web apps launched by Ludots through `Ludots.UI.Browser.Cef`. |
| UE5 BLUI | External UE5 adapter | Commercial engine transport adapter path. It hosts BLUI/CEF and forwards WebUI DataPlane messages, but it does not enter Core and does not own DataPlane semantics. |

Both paths consume the same high-level WebUI topics and event names. The difference is only the transport binding and browser lifecycle owner.

UE5 BLUI is therefore a reference transport shape, not a Core dependency:

- it may translate DataPlane messages into BLUI/CEF browser process messages;
- it may translate Web events back into `IWebUIBridge`;
- it may own UE textures, widgets, and native lifecycle;
- it must not define new Core contracts, collection semantics, or marker buffer semantics.

## 6 Boundary Rules

- `Ludots.WebUI` may depend on lower-level browser contracts to implement browser-backed transport.
- `Ludots.UI.Browser` must not depend on `Ludots.WebUI`.
- `Ludots.WebUI` must not duplicate `EntityCollectionStore`.
- `Ludots.WebUI` must not duplicate high-frequency marker buffers when existing SoA/bucket/drop-diagnostic infrastructure applies.
- Browser providers must not introduce new gameplay truth. They only carry messages, frames, input, resources, and lifecycle.
- UE5, BLUI, CEF native handles, platform windows, and texture objects stay inside adapter/provider assemblies.
- Browser-side caches are derived views. Their invalidation is driven by DataPlane revision, sequence, and diagnostics fields from Ludots.

## 7 Issue SSOT Anchors

Implementation issues should reference this page and ADR-0003 instead of redefining the boundary locally.

Use these issue slices when work is split:

- DataPlane vocabulary and Mod-facing API live in `Ludots.WebUI`.
- Collection topics are adapters over `EntityCollectionStore`.
- High-frequency marker/entity topics follow the minimap SoA/bucket/drop-diagnostic pattern.
- Ludots-started CEF and UE5 BLUI are separate transport adapters over the same DataPlane vocabulary.
- UE5 BLUI work must stay outside Core and must not introduce Core-facing UE or BLUI types.

## 8 Acceptance Evidence

Current evidence is architectural and source-aligned:

- Browser surface boundary: `docs/architecture/browser_ui_runtime.md`
- Browser ADR: `docs/adr/ADR-0003-browser-ui-runtime-contract.md`
- Entity collection store: `docs/architecture/entity_collection_query_infrastructure.md`
- Marker buffer model: `src/Core/Presentation/Minimap/MinimapMarkerBuffer.cs`
- Screen marker bucket model: `src/Core/Presentation/Minimap/MinimapScreenMarkerBuffer.cs`
- WebUI facade: `src/Libraries/Ludots.WebUI/`

Future implementation evidence should add focused tests around DataPlane topic revisioning, collection window payloads, high-frequency topic capacity/drop diagnostics, and transport parity between Ludots-started CEF and UE5 BLUI adapters.
