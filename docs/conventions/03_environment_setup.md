# Environment Setup and Build

This document summarizes the current Ludots SDK requirements, build commands, test commands, and launcher entrypoints.

The launcher command contract is defined by:

- `scripts/run-mod-launcher.cmd`
- `scripts/run-mod-launcher.ps1`

## 1. SDK Requirements

| SDK | Required | Reason |
|-----|----------|--------|
| .NET 8.0 | Yes | Primary target framework |
| .NET 9.0 | Yes | Multi-target dependencies still require it |
| .NET 10.0 preview | Yes | Some external multi-target dependencies still require it |
| Node.js + npm | Yes | Web launcher and web client builds |

Missing any required .NET SDK may break `dotnet restore`.

## 2. Common Build and Launch Commands

```powershell
# Build launcher CLI
dotnet build src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release

# Build web bridge
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release

# Inspect a launch plan
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib

# Launch a single mod
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib

# Launch multiple root mods on web
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance nav_playground --adapter web

# Launch the currently selected preset
.\scripts\run-mod-launcher.cmd cli launch --adapter web

# Export SDK ref DLLs
.\scripts\run-mod-launcher.cmd cli sdk export
```

Rules:

- The canonical wrapper form is `.\scripts\run-mod-launcher.cmd cli ...`.
- Selector inputs may be bindings, `mod:<id>`, `path:<mod-root>`, or `preset:<id>`.
- `launch` automatically resolves dependencies, DLLs, and runtime bootstrap.
- `game.json` is optional and only needed for direct adapter debugging.

## 3. Test Commands

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

## 4. Service Entrypoints

Ludots now treats web launcher and CLI as the product entry surface. They share `src/Tools/Ludots.Launcher.Backend`.

### 4.1 Product Web Launcher

```powershell
.\scripts\run-mod-launcher.cmd
```

This builds the React launcher, ensures the bridge is running, and opens the launcher URL.

### 4.2 Product CLI Launcher

```powershell
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
```

### 4.3 Direct Adapter Debug

```powershell
dotnet run --project src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -- launcher.runtime.json
dotnet run --project src/Apps/Web/Ludots.App.Web/Ludots.App.Web.csproj -c Release -- launcher.runtime.json
```

This path is for debugging only. Product usage should go through launcher CLI or web launcher.

## 5. Platform Notes

### 5.1 Linux / Raylib

- Native raylib and Skia dependencies must be present.
- Platform-specific native asset copy rules live in the relevant project files.

### 5.2 Cloud VM

- Raylib desktop usage may be limited by missing GPU or native graphics dependencies.
- CLI, bridge, web launcher, and the web adapter path are usable without a local desktop session.

## 6. Mod Paths and Workspaces

Mods are no longer restricted to the repository `mods/` folder.

Supported discovery paths:

- `launcher.config.json` scan roots
- `cli workspace add --path <dir>`
- `cli binding set <name> --path <mod-root>`

## 7. Related Docs

- [Launcher CLI Runbook](../reference/cli_runbook.md)
- [Startup Entrypoints](../architecture/startup_entrypoints.md)
- [Mod Architecture](../architecture/mod_architecture.md)
