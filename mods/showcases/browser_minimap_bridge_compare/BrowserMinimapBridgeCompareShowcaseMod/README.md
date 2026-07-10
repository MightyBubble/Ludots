# Browser Minimap Bridge Compare Showcase

This showcase compares minimap-marker browser UI delivery paths on the same deterministic 30k marker stream.

The CEF runtime now uses a Ludots-owned browser subprocess and native CEF/V8 provider. The True V8 Buffer lane calls `window.ludotsDataplane.acquireV8Buffer(...)`, which delegates to a render-process extension and accepts only a JavaScript `ArrayBuffer` returned by `CefV8Value::CreateArrayBufferFromBackingStore(...)`.

## Paths

- Read-Copy Buffer: `webui.minimapMarkers` emits WDMM binary marker rows through `BrowserSharedMemoryDataTransport`; the current CefSharp/bridge path reads a copied `byte[]` that JS normalizes to a byte view. This is the current baseline, not shared memory.
- Browser-owned ArrayBuffer: JS allocates `new ArrayBuffer(...)` and copies the same WDMM rows into it before decoding. This is a lower bound for browser-side typed-array parsing and canvas drawing. It is not native and does not satisfy the True V8 Buffer requirement.
- True V8 Buffer: this lane activates when the native CEF render-process provider maps the descriptor payload, copies it into a CEF `CefV8BackingStore`, and returns a JavaScript `ArrayBuffer` that consumes that backing store. Managed `byte[]`, array-like objects, `Uint8Array.from(...)` snapshots, and browser-created buffers are rejected.

## True V8 Buffer Requirement

A real V8 buffer path requires a native CEF render-process bridge that creates an ArrayBuffer on the V8 thread through CEF C++ backing-store APIs: `CefV8BackingStore::Create(...)` and `CefV8Value::CreateArrayBufferFromBackingStore(...)`. Returning `byte[]`, array-like objects, browser-owned `new ArrayBuffer(...)` copies, or `Uint8Array.from(...)` snapshots from CefSharp managed bindings does not satisfy this showcase.

The provider boundary is intentionally inside `Ludots.UI.Browser.Cef`. `Ludots.WebUI.DataPlane` and Core still publish only descriptors/capabilities; memory-map names and V8 details stay provider-private.

Minimal native bridge shape:

- Browser process owns DataPlane semantics and publishes descriptors as it does today.
- Render process installs `window.__ludotsCefV8.acquireV8Buffer(descriptor)` on the V8 thread.
- The JS facade exposes that as `window.ludotsDataplane.acquireV8Buffer(descriptor)`.
- The native bridge reads a provider-private shared registry, maps the named memory-mapped file, exact-matches the active descriptor, then fills a CEF V8 backing store and returns a JavaScript `ArrayBuffer`.
- JS decodes WDMM through `DataView` / typed-array views without `Uint8Array.from(...)`.
- Lifetime stays inside the CEF provider; Core and `Ludots.WebUI.DataPlane` only see transport capabilities and descriptors.

CEF 148 documents that `CefV8Value::CreateArrayBuffer(...)` returns `nullptr` when the V8 sandbox is enabled, so the bundled runtime cannot expose an external memory-mapped pointer directly as a JS ArrayBuffer. The showcase therefore uses the CEF 146+ V8 backing-store API instead. It is a real native V8 ArrayBuffer path and works with the sandbox, but it is not external shared memory directly aliased into JS.

The showcase must not call `CreateArrayBufferWithCopy(...)`.

BLUI is not a shortcut for this path. Its public CEF integration is an offscreen paint buffer to UE texture upload path, not a JS/V8 ArrayBuffer shared-memory bridge.

The Read-Copy Buffer and Browser-owned ArrayBuffer lanes are comparison baselines only.

## Controls

Environment variables:

- `LUDOTS_MINIMAP_BRIDGE_MARKERS`: marker count, clamped to 1,000..60,000. Default: 30,000.
- `LUDOTS_MINIMAP_BRIDGE_HZ`: publish rate, clamped to 1..60. Default: 10.

The browser app intentionally renders small markers only. It does not render a heatmap.
