# ADR-0003 Browser UI Runtime Contract

## Status

Accepted

## Context

Ludots already has a native C# UI runtime with Compose, Reactive, and HTML/CSS Markup authoring. ADR-0002 intentionally keeps that native runtime JavaScript-free and prevents Markup from becoming a second browser-like renderer.

Some product surfaces still need to run real web applications: JavaScript, DOM APIs, web framework bundles, WASM, and browser resource behavior. A previous UE5-side approach chained UE widget -> BLUI/CEF web app -> C++ -> C# binding -> Ludots, which creates too much adapter-specific glue and keeps the browser boundary outside the Ludots C# architecture.

The current Core repository no longer owns a UE5 adapter. Commercial engine adapters are external to Core, while Core may define platform-neutral contracts that adapters consume.

## Decision

Add a browser-backed UI runtime contract split into two assemblies:

1. `Ludots.UI.Browser`
   - Platform-neutral browser contracts and Ludots UI canvas integration.
   - Frame, dirty rect, viewport, navigation, input, resource resolver, lifecycle, and message bridge types.
   - Provider identity and capability contracts for the two built-in providers: CEF and Ultralight.
   - BCL-only packaged web app resource resolver for local `index.html` / JS / CSS / WASM assets.
   - Browser canvas content that routes pointer, keyboard, focus, viewport resize, and alpha hit-test through `UiScene`.
   - May depend on platform-neutral `Ludots.UI`; no Skia, CEF native, Ultralight native, Raylib, UE5, or commercial engine dependency.
2. `Ludots.UI.Browser.Skia`
   - Optional Skia adapter for drawing `BrowserFrame` pixels through the existing `Ui.Canvas(IUiCanvasContent)` path.
   - Depends on `Ludots.UI`, `Ludots.UI.Browser`, `Ludots.UI.Skia`, and SkiaSharp.

Platform adapters may bypass Skia for browser pixels when they can upload browser dirty rectangles directly into native textures. The Raylib adapter owns that performance path; the browser remains a `Ui.Canvas(...)` payload for layout, hit testing, focus, and input capture.

`Ludots.UI.Skia` exposes `ISkiaUiCanvasContent` so additional Skia-backed canvas payloads can be rendered without changing the platform-neutral `Ludots.UI` marker interface.

Concrete CEF and Ultralight implementations are browser engine provider assemblies behind `IBrowserRuntime` and `IBrowserSurface`. They are not part of this ADR's Core implementation.

Formal built-in provider assemblies:

- `Ludots.UI.Browser.Cef`: full Chromium compatibility baseline.
- `Ludots.UI.Browser.Ultralight`: lightweight game UI provider for Ludots-owned web bundles.

No provider outside CEF and Ultralight is part of the formal Core provider set for this architecture.

Existing `Ludots.WebUI` bridge abstractions remain as the Mod-facing event facade. They should be implemented on top of `IBrowserMessageBridge` when a browser surface is used, rather than becoming a parallel browser runtime.

Add a WebUI DataPlane boundary above Browser UI:

- `Ludots.WebUI` owns DataPlane topics, event names, Mod-facing lifecycle, and API exposure.
- `EntityCollectionStore` is the required foundation for collection-backed WebUI topics and window queries.
- `MinimapMarkerBuffer` and `MinimapScreenMarkerBuffer` are the required design precedent for high-frequency marker/entity topics: SoA arrays, explicit capacity, style/data buckets, stable ids, staged materialization, and drop diagnostics.
- `IBrowserMessageBridge` is a transport for DataPlane messages, not the owner of DataPlane semantics.
- Ludots-started CEF and external UE5 BLUI are two transport paths for the same DataPlane vocabulary.
- UE5 BLUI remains an adapter concern. It may host BLUI/CEF and forward messages, but it does not enter Core and must not define browser, collection, marker, or topic contracts.

The WebUI DataPlane also defines a Browser Native Bridge shape:

- Web applications depend only on Ludots-owned browser facades: generic host messages use `window.ludotsBrowser`, and DataPlane messages use `window.ludotsDataplane`.
- `IWebUiDataTransport` is the C# semantic boundary for control messages, delivery semantics, capability negotiation, shared-buffer descriptors, and diagnostics.
- The control lane carries handshake, subscribe, command, ack/error, diagnostics, and buffer descriptor messages.
- The shared-memory data lane is optional and must be explicitly negotiated; missing capability fails fast instead of falling back silently to message/base64.
- CEF renderer/V8 injection is provider implementation detail. It may install `window.ludotsBrowser` and `window.ludotsDataplane` over provider-native messaging, but Core, Browser contracts, WebUI contracts, and Web apps do not depend on V8 or CEF-specific globals.
- UE5 BLUI may install the same facade over BLUI-native messages and shared-buffer descriptors. It remains a host adapter and must not own Ludots topic or command semantics.

## Consequences

Positive:

- CEF and Ultralight can be integrated through the same C# surface contract.
- Browser UI can reuse existing `UiScene`, layout, canvas, hit-test, focus, and input paths.
- Raylib can render browser pixels through a direct texture path instead of uploading them through Skia.
- Web app communication has one explicit bridge instead of an ad hoc C++/C#/adapter chain.
- Core remains free of UE5 and CEF-specific ownership.

Constraints:

- Native Markup still does not execute JavaScript.
- Browser UI is not a replacement for native Compose / Reactive / Markup.
- Engine adapters must own native browser process lifecycle and platform input conversion.
- CEF provider bootstrap is Ludots host/runtime infrastructure. Application Mods may request or require browser runtime capability and consume `IBrowserRuntime`, but they must not package, locate, initialize, register, or unload CEF.
- Engine adapters that add a direct texture path must keep input and alpha hit-test routed through `BrowserSurfaceCanvasContent` / `UIRoot`; direct rendering must not become a second interaction system.
- CEF is the compatibility baseline for arbitrary web apps; Ultralight is a lightweight provider, not a Chromium-equivalent compatibility promise.
- CEF process lifetime is not a per-runtime-owner lifecycle. `IBrowserRuntime.DisposeAsync`, mod unload, and editor play-session teardown release Ludots-owned surfaces but must not call `Cef.Shutdown()`. Any explicit CEF shutdown hook must be host-owned and terminal.
- CEF custom scheme handler state must be process-stable across host ALC reloads. A scheme handler registered during the first editor session must be able to resolve surfaces created by later sessions in the same process.
- Host adapters that load a Ludots-owned browser provider assembly from `browserRuntime.providerAssemblyPath` must use `Ludots.UI.Browser.BrowserRuntimeProviderLoader`. This is a host bootstrap responsibility, not a Mod responsibility, and must not hardcode CefSharp assembly names, load the provider into the default ALC, or fall back to the Mod load plan.
- UE5 bridge hosts must call the Ludots-owned CEF bootstrap before game/session start when browser runtime is required. The bridge must treat `browserRuntime.providerAssemblyPath` as a provider package root by using the shared loader's shadow-copy provider ALC path: managed providers default to a collectible ALC, while CEF uses a non-collectible provider ALC and process-shared `CefSharp` assembly prefix because CefSharp mixed/native runtime callbacks cannot be split across collectible/provider contexts. UE PIE teardown must not unload or reinitialize CEF through a Mod.
- Higher-level Ludots API exposure must be layered above `IBrowserMessageBridge`.
- `Ludots.WebUI` must remain above Browser UI as an event/API facade; `Ludots.UI.Browser` must not depend on it.
- WebUI collection topics must reuse `EntityCollectionStore`; unknown owner/key/query ids fail explicitly.
- High-frequency WebUI topics must follow the minimap buffer pattern for capacity, buckets, stable ids, and drop diagnostics instead of allocating per row on the hot path.
- UE5 BLUI support is transport adapter work only. Core accepts the transport-neutral C# contracts and DataPlane vocabulary, not UE widgets, UE textures, or BLUI native lifecycle.
- Browser Native Bridge support does not make V8 a Ludots dependency. V8 is a browser-engine implementation detail behind CEF/BLUI.
- Web app source and shipped Mod assets must not call provider private globals such as CEF/CefSharp or BLUI-specific objects directly; they fail fast when the Ludots facade is absent.
- Shared-memory support requires benchmark evidence under `artifacts/benchmarks/webui-dataplane` and host conformance evidence for input, focus, alpha hit-test, and passthrough semantics.

## Evidence

- Contracts: `src/Libraries/Ludots.UI.Browser/`
- Skia adapter: `src/Libraries/Ludots.UI.Browser.Skia/`
- Canvas extension point: `src/Libraries/Ludots.UI.Skia/ISkiaUiCanvasContent.cs`
- WebUI DataPlane boundary: `docs/architecture/webui_dataplane_architecture.md`
- DataPlane contracts and tests: `src/Libraries/Ludots.WebUI.DataPlane/`, `src/Tests/WebUiDataPlaneTests/`
- Tests: `src/Tests/UiBrowserTests/`
