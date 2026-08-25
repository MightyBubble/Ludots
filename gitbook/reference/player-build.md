# 玩家发行链（零安装）

本页定义 epic #1190 族 D+E 落地后的玩家发行与 Mod 分发契约。

## 1 玩家包

- 产出命令（开发机，需要 .NET 9 SDK）：

```powershell
.\scripts\publish-player-build.ps1                        # 全量 mods
.\scripts\publish-player-build.ps1 -Mods ExampleMod       # 指定 mods（自动补 LudotsCoreMod）
```

- 玩家机**不需要**安装 .NET SDK / 运行时 / Node.js：
  - Raylib 应用与 Launcher.Cli 均为 `--self-contained` 发布，自带运行时；
  - 启动器检测到 apphost exe（`Ludots.App.Raylib.exe`）时直接启动，不再走 `dotnet exec`；
  - 包内 mods 为 BinaryOnly（无 `.cs`/`.csproj`），配合 `--build never` 全链免编译。

- 包结构（复刻仓库相对布局，`FindRepoRoot` 以 `assets/` 定位包根）：

```
<pkg>/
  Play.cmd                    # 双击入口：launch --adapter raylib --build never <默认 selector>
  README-PLAYER.md
  launcher.config.json / launcher.presets.json
  assets/
  mods/<ModName>/             # BinaryOnly：mod.json + bin/net9.0/*.dll + 资源
  src/Apps/Raylib/.../bin/Release/net9.0/   # 自包含应用（apphost exe + dll）
  tools/launcher/Ludots.Launcher.Cli.exe    # 自包含启动器
```

- 默认 selector 为 `mod:LudotsCoreMod mod:ExampleMod`：LudotsCoreMod 提供 presentation/startup 的 game.json 基座，缺它会报 `game.json presentation must be explicitly configured`。

## 2 Mod 作者发布（BinaryOnly 包）

```powershell
.\scripts\pack-mod.ps1 -ModDir mods\MyMod       # 产出 dist\mods\MyMod（可直接分发给玩家）
```

- 包内容：`mod.json` + `bin/net9.0/MyMod.dll` + 资源；脚本强制校验无 `.cs`/`.csproj` 泄漏。
- 玩家把包目录放进玩家包 `mods/` 后，用 `tools\launcher\Ludots.Launcher.Cli.exe launch --adapter raylib --build never mod:MyMod ...` 启动。
- 约定：玩家默认拿预编译包；BuildableSource 现场编译仅保留给开发者/调试（`--build auto`）。

## 3 边界与后续

- 发行脚本当前只打 Raylib 平台；Web 平台仍依赖 Node（开发向）。
- 无 SDK 的源码编译已由 `scripts/compile-mod.ps1`（内嵌 Roslyn）落地，契约见 [零环境/零网络契约](zero-env-setup.md)。
