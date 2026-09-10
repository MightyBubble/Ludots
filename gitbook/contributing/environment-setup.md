# 环境与构建

本页定义当前 Ludots 的正式环境要求、构建命令和启动入口。

## 1 SDK 要求

- .NET 9.0（`global.json` 固定 SDK `9.0.100`，`rollForward: latestFeature`，全仓正式 target `net9.0`）
- 编维护测试图还要 .NET 8 SDK：vendored 图库（`Svg.Skia` / `SkiaSharp` / `LiteNetLib` / `ExCSS`）仍是 `net8.0`。targeting pack 必须来自本机 SDK 8，不能去 nuget.org；离线源里也没有 8.0 App.Ref。
- CI 四个 .NET 工作流走 `.github/actions/setup-ludots-dotnet`：认 `global.json` 装 9，再装 `8.0.x`。不要在 yml 里另漂一个 `9.0.x`。
- Node.js + npm

## 2 常用构建命令

```powershell
dotnet build src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj -c Release
```

## 3 常用启动命令

```powershell
.\scripts\run-mod-launcher.cmd
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
```

规则：

- 正式入口是 launcher web 和 launcher CLI。
- `.\scripts\run-mod-launcher.cmd cli ...` 是 canonical wrapper 形式。
- 直接运行 `src/Apps/...` 只用于 adapter 级调试。

## 4 常用测试命令

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj
dotnet test src/Tests/PresentationTests/PresentationTests.csproj
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj
```

## 5 平台说明

- Linux / Raylib 需要本机原生依赖。
- Cloud VM 更适合 CLI、bridge 和 web adapter。
- Mod 不局限于仓库 `mods/` 目录，launcher 支持外部 scan roots、workspace 和 binding。

## 6 深度材料

- 仓库深度版：`docs/conventions/03_environment_setup.md`
- CLI 手册：`docs/reference/cli_runbook.md`
