# AGENTS.md

## Cursor Cloud specific instructions

### Overview

Ludots is a C# ECS game engine/gameplay framework. The codebase has two main runnable services for development:

- **Editor Bridge** — ASP.NET Core API (port 5299), serves mod/map/terrain data
- **Editor React** — Vite + React frontend (port 5173), visual map editor connecting to Bridge

The Raylib desktop app requires native `libraylib.so` on Linux (only `raylib.dll` for Windows is in the repo), so it cannot run in the Cloud VM. Tests and the Editor stack work fully.

### SDK requirements

The codebase needs **.NET 8.0**, **.NET 9.0**, and **.NET 10.0 (preview)** SDKs installed — the vendored DotRecast library multi-targets `netstandard2.1;net8.0;net9.0;net10.0`. Without all three SDKs, `dotnet restore` will fail.

SDKs are installed to `/usr/share/dotnet` via the official `dotnet-install.sh` script, symlinked at `/usr/local/bin/dotnet`. The `PATH` and `DOTNET_ROOT` are set in `~/.bashrc`.

### Running services

```bash
# Editor Bridge (ASP.NET Core API)
dotnet run --project src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj

# Editor React (Vite dev server)
cd src/Tools/Ludots.Editor.React && npx vite --host 0.0.0.0 --port 5173
```

### Running tests

```bash
dotnet test src/Tests/GasTests/GasTests.csproj           # 589 tests
dotnet test src/Tests/Navigation2DTests/Navigation2DTests.csproj  # 28 tests
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj  # 1 test
```

### Raylib desktop app on Linux

The Raylib desktop app now works on Linux. Key changes:

- `libraylib.so` (raylib 5.5, x64) is checked into `src/Platforms/Desktop/`
- `SkiaSharp.NativeAssets.Linux` NuGet package provides `libSkiaSharp.so` for UI rendering
- The csproj uses OS-conditional `<ItemGroup>` to copy the correct native lib per platform
- System dependencies needed: `libx11-dev`, `libxrandr-dev`, `libxi-dev`, `libxcursor-dev`, `libxinerama-dev`, `libgl1-mesa-dev`

Run with: `dotnet run --project src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json`

### Known issues on Linux

- `src/Tools/Ludots.Editor.React/src/App.tsx` has a case-sensitive import (`@/Components/...` vs `@/components/...`). A fix has been committed on this branch. Without the fix, `tsc` type-checking fails and Vite may fail to resolve the module.
- ESLint reports ~71 pre-existing errors in the React Editor (mostly `@typescript-eslint/no-explicit-any` and `no-case-declarations`). These are pre-existing, not introduced by environment setup.
