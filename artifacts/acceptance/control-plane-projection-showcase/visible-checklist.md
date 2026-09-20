# Visible Checklist: control-plane-projection-showcase

## Evidence Captured In This Run
- Headless acceptance: PASS via `ControlPlaneProjectionShowcaseAcceptanceTests`.
- Launcher binding: PASS for `control_plane_projection_showcase` in `launcher.config.json`.
- Packaged WebApp assets: PASS for `assets/control-plane-app/index.html` plus JS/CSS bundles.
- DataPlane contract: covered by `ControlPlaneProjectionDataPlaneTests` and the WebApp client test/build commands run outside this test.
- GUI recording: NOT completed by this headless run.

## Surface Ownership
- Owner: `ControlPlaneProjection.Showcase` lease on `UiSurfaceSegment.Overlay`.
- Acquire path: `ControlPlaneProjectionDataPlaneInstaller.TryInstallAsync` after `GameStart` scenario installation, only when `IBrowserRuntime` exists.
- Restore/release path: `ControlPlaneProjectionDataPlaneInstallation.Dispose()` releases the lease; `ControlPlaneProjectionShowcaseModEntry.OnUnload()` disposes the installation.
- Headless branch: when `IBrowserRuntime` is absent, installer returns null and no overlay surface is acquired.

## First-Frame Readability
- WebApp panel root is a fixed 420x360 overlay canvas at x=18, y=96.
- React panel default state renders a readable `Control Plane` header, owned/proxy/view counts, and transport status before snapshots arrive.
- This run validates build/package readiness but does not replace a real CEF first-frame screenshot.

## Interaction Safety
- Headless acceptance keeps `CoreServiceKeys.UiCaptured=false` while O-key and selection flow run.
- The O-key path and WebUI `toggleProxy` command share `ControlPlaneProjectionScenarioState.ToggleProxy()`.
- Manual GUI UAT must still verify world click/box selection and camera movement while the browser panel is visible.

## Visible UAT Status
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch control_plane_projection_showcase --adapter raylib`
- Required environment: Windows GUI with raylib adapter and CEF browser runtime provider available.
- Recording script: load map -> box-select mixed P1/P2 units -> press O -> verify dark-green owned ring and light-green proxied ring -> press O again -> verify proxied ring clears.
- CEF panel script: verify subscription to `ludots.showcase.control_plane.state`, visible owned/proxy counts, and `toggleProxy` command acknowledgement.
- Status: completed in the visible-UAT pass; see `artifacts/rfc0065-visible-uat/control-plane-projection-cef/a1_cef_final2_*.png`.
