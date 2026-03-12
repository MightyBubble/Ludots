# Launcher CLI Runbook

This document is the single source of truth for the current Ludots launcher CLI.

Product entrypoints:

- Visual launcher: `.\scripts\run-mod-launcher.cmd`
- CLI launcher: `.\scripts\run-mod-launcher.cmd cli ...`

Both entrypoints reuse the same backend:

- `src/Tools/Ludots.Launcher.Backend/LauncherService.cs`
- `src/Tools/Ludots.Editor.Bridge/Program.cs`

## 0. Quick Start

If you only need to run mods, use these product commands:

```powershell
# Single mod on raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib

# Single mod on web
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web

# Multi-mod on raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance nav_playground --adapter raylib

# Multi-mod on web
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance nav_playground --adapter web
```

If you want a reusable launch target:

```powershell
.\scripts\run-mod-launcher.cmd cli preset save --name camera-nav-web camera_acceptance nav_playground --adapter web
.\scripts\run-mod-launcher.cmd cli preset select preset_camera-nav-web
.\scripts\run-mod-launcher.cmd cli launch --adapter web
```

Rules:

- `launch` is the product command. It resolves dependencies, DLLs, SDK refs, and runtime bootstrap automatically.
- `launch` with no selectors uses the currently selected preset.
- Pass `--adapter` explicitly in scripts and reproducible runs.

## 1. State Files

Launcher state is split into separate files with non-overlapping responsibilities.

- `launcher.config.json`
  Repository-level scan roots, bindings, default adapter, and project hints.
- `launcher.presets.json`
  Repository-level named launch presets.
- `%AppData%/Ludots/Launcher/preferences.json`
  User preferences such as the last selected adapter or preset.
- `%AppData%/Ludots/Launcher/config.overlay.json`
  User-local overlay for extra scan roots, bindings, and hints without mutating repository config.

Runtime bootstrap is separate:

- `launcher.runtime.json`
  Written by `launch`; contains only adapter bootstrap data such as `ModPaths`.
- `game.json`
  Optional direct-debug bootstrap only. Product launch flows do not require manual `gamejson write`.

Runtime gameplay configuration still comes from the merged config pipeline:

- `assets/Configs/game.json`
- `<Mod>/assets/game.json`
- `<Mod>/assets/Configs/game.json`

## 2. Selector Model

The CLI accepts selectors instead of assuming that every mod must live under `mods/`.

Supported selectors:

```text
$camera_acceptance
camera_acceptance
mod:CameraAcceptanceMod
path:mods/fixtures/camera/CameraAcceptanceMod
preset:camera_acceptance_web
```

Rules:

- `$alias`
  Resolve a binding from `launcher.config.json`.
- `alias`
  PowerShell-friendly shorthand. If a binding with the same name exists, resolve it as the binding; otherwise resolve it as `mod:<id>`.
- `mod:<ModId>`
  Resolve by manifest id.
- `path:<mod-root>`
  Resolve a mod from any explicit path.
- `preset:<presetId>`
  Expand a saved preset into one or more selectors.

A single `resolve` or `launch` command may accept multiple selectors.

## 3. Common Commands

### 3.1 Resolve

```powershell
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance nav_playground --adapter web
.\scripts\run-mod-launcher.cmd cli resolve --mod CameraAcceptanceMod --mod Navigation2DPlaygroundMod --adapter raylib --json
```

`resolve` surfaces:

- `rootMods`
- `orderedMods`
- startup diagnostics such as `defaultCoreMod`, `startupMapId`, and `startupInputContexts`
- warnings for multi-root conflicts and the final winning config source

If multiple root mods define `startupMapId`, only one startup map is selected at runtime. Always run `resolve` before `launch` when reproducing multi-mod behavior.

### 3.2 Launch

```powershell
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter web
```

Launch behavior:

- Dependencies are resolved automatically.
- Main DLLs and dependent DLLs are resolved automatically.
- SDK ref DLL export is handled by the launcher backend.
- `launcher.runtime.json` is written automatically for the selected adapter app.

### 3.3 Multi-Mod Launch

```powershell
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance nav_playground --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance nav_playground --adapter web
```

Rules:

- Multi-mod launch is supported.
- Dependency closure is computed over the combined root set.
- Runtime still enters a single startup map; use `resolve` diagnostics to see which mod wins.
- For reproducible runs, pass `--adapter` explicitly.

### 3.4 Workspace and Bindings

```powershell
.\scripts\run-mod-launcher.cmd cli workspace list
.\scripts\run-mod-launcher.cmd cli workspace add --path ..\ExternalMods

.\scripts\run-mod-launcher.cmd cli binding list
.\scripts\run-mod-launcher.cmd cli binding set camera_acceptance --path mods/fixtures/camera/CameraAcceptanceMod --project CameraAcceptanceMod.csproj
.\scripts\run-mod-launcher.cmd cli binding set nav_playground --path mods/Navigation2DPlaygroundMod --project Navigation2DPlaygroundMod.csproj
```

Notes:

- `workspace add` extends recursive scan roots.
- `binding set` creates an explicit global-name-to-path mapping.
- A bound mod may live anywhere, inside or outside the repository.
- `--project` is only a hint; dependency and DLL resolution still come from the backend.

### 3.5 Presets

```powershell
.\scripts\run-mod-launcher.cmd cli preset list
.\scripts\run-mod-launcher.cmd cli preset save --name camera-web camera_acceptance --adapter web
.\scripts\run-mod-launcher.cmd cli preset save --name camera-nav-raylib camera_acceptance nav_playground --adapter raylib
.\scripts\run-mod-launcher.cmd cli preset select preset_camera-nav-raylib
.\scripts\run-mod-launcher.cmd cli launch --adapter raylib
```

Presets store selector sets, not expanded final mod lists. Dependency closure is recalculated on every `resolve` or `launch`.

Notes:

- `preset select` changes the default selector set for later `resolve`, `build`, and `launch`.
- Use `launch --adapter ...` after selecting a preset when you need a deterministic adapter choice.

### 3.6 Build and SDK

```powershell
.\scripts\run-mod-launcher.cmd cli build camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli build app --adapter web
.\scripts\run-mod-launcher.cmd cli sdk export
.\scripts\run-mod-launcher.cmd cli mod fix-project CameraAcceptanceMod
.\scripts\run-mod-launcher.cmd cli mod solution CameraAcceptanceMod
```

Notes:

- `build` is still available, but ordinary users should prefer `launch`.
- `sdk export` keeps developer and player launch flows aligned around the same product path.

## 4. Adapter Rules

- Use `--adapter raylib|web` explicitly in reproducible commands.
- Web launcher and CLI both call the same backend launch plan logic.
- `game.json` is optional and only relevant when bypassing the launcher to debug an adapter app directly.
- The canonical wrapper form is `.\scripts\run-mod-launcher.cmd cli ...`. Do not write `-- cli ...`.

## 5. Current Technical Debt

Correctness is now in a better state, but web performance is not closed yet.

- Web uses correctness-first full self-contained presentation snapshots.
- The current transport is still lossy latest-frame delivery.
- Browser-side world, HUD, and UI application still compete on the main thread.

See:

- `artifacts/techdebt/2026-03-12-web-ui-snapshot-pipeline.md`

## 6. Related Docs

- [Environment Setup](../conventions/03_environment_setup.md)
- [Startup Entrypoints](../architecture/startup_entrypoints.md)
- [Unified Launcher RFC](../rfcs/RFC-0001-unified-launcher-cli-and-workspace.md)
