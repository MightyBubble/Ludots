# Browser Runtime Provider Adapter Guide

This page is the adapter-facing checklist for hosts that need Ludots Browser UI through a provider assembly such as `Ludots.UI.Browser.Cef.dll`.

Use it for Raylib-like hosts, external UE5 bridge hosts, Unity/Godot hosts, and any custom desktop host that consumes `browserRuntime.providerAssemblyPath`.

## Decision

Browser engine providers are host bootstrap infrastructure, not Mods.

An adapter must load provider implementations through `Ludots.UI.Browser.BrowserRuntimeProviderLoader`. The adapter must not directly load the provider DLL from a source `bin/` directory, must not load the provider into `AssemblyLoadContext.Default`, and must not resolve CefSharp or other provider-private dependencies through the Mod load plan.

## Reuse List

Adapters reuse:

- `IBrowserRuntime` as the host-visible browser runtime contract.
- `IBrowserRuntimeHostLifecycle` as the terminal host-exit lifecycle hook.
- `BrowserRuntimeServiceNames.BrowserRuntime` and `BrowserRuntimeServiceNames.HostLifecycle` in the host service dictionary.
- `BrowserRuntimeProviderLoader` and `BrowserRuntimeProviderLoadOptions` for shadow-copy loading and provider ALC ownership. Default managed providers use a collectible ALC; CEF uses a non-collectible provider ALC because CefSharp includes mixed/native runtime assemblies that cannot be loaded into a collectible ALC. CEF also declares `CefSharp` as a process-shared assembly prefix so CefSharp managed/runtime callback assemblies load once in the Default ALC from the shadow-copied provider package, while the provider host assembly itself stays in the provider ALC.
- `browserRuntime.providerAssemblyPath`, `browserRuntime.runtimeRootPath`, and `browserRuntime.cacheRootPath` from the resolved launcher/runtime config.
- `BrowserSurfaceCanvasContent` / `UIRoot` for browser input, focus, resize, and alpha hit-test routing.

Adapters do not add a parallel provider registry, a second browser runtime service name, or a Mod-owned CEF bootstrap path.

## Required Bootstrap Shape

1. Resolve `browserRuntime` from the host's already-resolved app/runtime config.
2. If `enabled=false` and `required=true`, fail fast.
3. If `enabled=true`, require a known provider id. Built-in providers: `cef` (Windows Chromium compatibility) and `ultralight` (cross-platform lightweight game UI; use this on cloud Linux).
4. Resolve `providerAssemblyPath` relative to the host base directory when it is not rooted.
5. Resolve `runtimeRootPath` relative to the host base directory when it is not rooted. For provider-loader installs it is required and must be the provider package root or a child of that package.
6. Verify the provider assembly exists. Missing provider assemblies fail fast.
7. Call `BrowserRuntimeProviderLoader.Install(...)` from the host composition root before gameplay/session code consumes browser services.
8. Keep `Ludots.UI.Browser` contracts in the host ALC. The provider ALC must share those contract assemblies so `handle.Runtime is IBrowserRuntime` is true in the host.
9. Store the returned `IBrowserRuntime` in the host/global service dictionary through the loader-owned path.
10. Store or preserve the loader-provided `IBrowserRuntimeHostLifecycle` service for terminal host exit.

The CEF provider preflights its own runtime package before touching CefSharp process state. Missing managed CefSharp assemblies, browser subprocess files, Chromium resource packs, native CEF/D3D/Vulkan libraries, `libcef.dll`, `resources.pak`, `icudtl.dat`, or `locales/en-US.pak` fail with one Ludots error listing every missing path. Hosts should pass `runtimeRootPath` as the provider package root and should not duplicate this provider-private native layout check.

Minimal shape:

```csharp
BrowserRuntimeProviderLoadHandle handle = BrowserRuntimeProviderLoader.Install(
    new BrowserRuntimeProviderLoadOptions(
        services,
        providerAssemblyPath,
        "Ludots.UI.Browser.Cef.CefBrowserRuntimeHost")
    {
        ProviderId = "cef",
        UseCollectibleLoadContext = false,
        ProcessSharedAssemblyNamePrefixes = new[] { "CefSharp" },
        RuntimeRootPath = runtimeRootPath,
        BrowserCacheRootPath = cacheRootPath,
        ShadowCopyRootPath = optionalAdapterCacheRoot,
        Log = message => adapterLog.Info(message)
    });

IBrowserRuntime runtime = handle.Runtime;
```

## Lifecycle Rules

Provider loading has two lifetimes:

- Session lifetime: surfaces and game/session references.
- Host-process lifetime: browser engine process state, provider ALC, native process hooks, and terminal shutdown.

On ordinary editor play-session teardown, dispose session-owned surfaces/runtime facades that will not be reused, but do not call terminal CEF shutdown if the host process may create another browser runtime later. CEF cannot be reinitialized after `Cef.Shutdown()` in the same process.

On terminal host process exit, call the service registered under `BrowserRuntimeServiceNames.HostLifecycle`:

```csharp
if (services.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? lifecycle) &&
    lifecycle is IBrowserRuntimeHostLifecycle browserLifecycle)
{
    browserLifecycle.ShutdownProcessForHostExit();
}
```

The loader lifecycle disposes the provider runtime, calls the provider terminal lifecycle, restores/removes loader-owned services, and then follows the provider ALC policy. Collectible provider ALCs are unloaded and collection is logged. Non-collectible provider ALCs are kept process-scoped and logged as such.

Long-lived editor hosts should install the provider once per host process or own an explicit process-level provider handle. Do not create a fresh CEF provider for every play session and then call terminal shutdown on each session end.

## Rendering And Input

Adapters may render browser pixels through the Skia path or a native direct texture path.

Direct texture upload is allowed only for pixels. It must not become a second interaction system:

- Browser pointer, wheel, keyboard, focus, resize, and alpha hit-test must still route through `BrowserSurfaceCanvasContent` / `UIRoot`.
- Web app assets must use Ludots facades such as `window.ludotsBrowser` and `window.ludotsDataplane`.
- Provider-private globals such as CefSharp, CEF V8 bindings, BLUI objects, or engine widget objects are adapter-private.

## Forbidden Adapter Patterns

Do not:

- Call `AssemblyLoadContext.Default.LoadFromAssemblyPath(providerAssemblyPath)` from an adapter installer.
- Call `Assembly.LoadFrom(providerAssemblyPath)` or equivalent direct load APIs on the source build output.
- Load `Ludots.UI.Browser.Cef.dll` from `src/Libraries/.../bin/...` without shadow-copy isolation.
- Resolve CefSharp, CEF native files, or provider-private dependencies from the Mod load plan.
- Package CEF as a Mod or make Mod load/unload own CEF initialization.
- Hardcode CefSharp assembly names in host adapters.
- Add a fallback provider when the requested provider is missing.
- Call `Cef.Shutdown()` from `IBrowserRuntime.DisposeAsync`, surface disposal, Mod unload, or ordinary editor play-session teardown.
- Let web app or Mod source call provider-private globals directly.

## Validation Checklist

Before an adapter change is accepted:

- Missing provider assembly fails fast with no fallback.
- Missing `runtimeRootPath` fails fast instead of falling back to provider `Assembly.Location`.
- `runtimeRootPath` outside the provider package fails fast instead of bypassing shadow-copy ownership.
- Incomplete CEF runtime roots fail before CEF initialization and list every missing required path.
- The returned runtime is assignable to the host `IBrowserRuntime`.
- Provider private dependencies resolve from the shadow-copied provider package.
- Changing a provider-private DLL/native file creates a new shadow-copy cache entry.
- The source provider DLL can be overwritten or deleted while the host process remains alive.
- Terminal host exit logs either `collectible ALC collected=...` or `non-collectible provider ALC`, depending on the provider ALC policy.
- Adapter installer source does not contain direct Default ALC provider loads.
- Browser input still flows through `UIRoot`; direct texture rendering does not bypass hit-test or focus ownership.
- Web app source uses Ludots facades only.

Recommended regression tests:

```text
dotnet test src\Tests\RaylibAdapterTests\RaylibAdapterTests.csproj --filter BrowserRuntimeProviderLoaderTests
dotnet test src\Tests\BrowserCefTests\BrowserCefTests.csproj --filter "CefBrowserRuntimeHostTests|CefBrowserRuntimeArchitectureTests|CefBrowserRuntimeAssemblyResolutionTests"
dotnet test src\Tests\GasTests\GasTests.csproj --filter "RaylibHost_OwnsTerminalBrowserRuntimeShutdown|RaylibBrowserRuntimeInstaller_ResolvesProviderDependenciesThroughProviderPackage"
```
