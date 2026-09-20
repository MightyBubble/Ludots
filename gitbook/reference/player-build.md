# 玩家发行链（零安装）

本页定义 epic #1190 族 D+E 落地后的玩家发行与 Mod 分发契约。

## 1 玩家包

- 产出命令（开发机，需要 .NET 9 SDK；Windows PowerShell 或跨平台 pwsh）：

```powershell
.\scripts\publish-player-build.ps1                              # 按当前 OS 选择 RID
.\scripts\publish-player-build.ps1 -Mods ExampleMod             # 指定 mods（自动补 LudotsCoreMod）
.\scripts\publish-player-build.ps1 -RuntimeIdentifier linux-x64 # 显式 RID
```

默认 RID：`win-x64` / `linux-x64` / `osx-x64` / `osx-arm64`（按打包机 OS 自动选择）。

- 玩家机**不需要**安装 .NET SDK / 运行时 / Node.js：
  - Raylib 应用与 Launcher.Cli 均为 `--self-contained` 发布，自带运行时；
  - 启动器优先直启 apphost（Windows：`Ludots.App.Raylib.exe`；Linux/macOS：无扩展名 `Ludots.App.Raylib`）；
  - 包内 mods 为 BinaryOnly（无 `.cs`/`.csproj`），配合 `--build never` 全链免编译。

- 包结构（复刻仓库相对布局，`FindRepoRoot` 以 `assets/` 定位包根）：

```
<pkg>/
  Play.cmd | Play.sh          # 入口：launch --adapter raylib --build never <默认 selector>
  README-PLAYER.md
  launcher.config.json / launcher.presets.json
  assets/
  mods/<ModName>/             # BinaryOnly：mod.json + bin/net9.0/*.dll + 资源
  src/Apps/Raylib/.../bin/Release/net9.0/   # 自包含应用（apphost + dll）
  tools/launcher/Ludots.Launcher.Cli[.exe]  # 自包含启动器
```

- 默认 selector 为 `mod:LudotsCoreMod mod:ExampleMod`：LudotsCoreMod 提供 presentation/startup 的 game.json 基座，缺它会报 `game.json presentation must be explicitly configured`。

## 2 Mod 作者发布（BinaryOnly 包）

```powershell
.\scripts\pack-mod.ps1 -ModDir mods\MyMod       # 产出 dist\mods\MyMod（可直接分发给玩家）
```

- 包内容：`mod.json` + `bin/net9.0/MyMod.dll` + 资源；脚本强制校验无 `.cs`/`.csproj` 泄漏。
- 玩家把包目录放进玩家包 `mods/` 后启动：`tools/launcher/Ludots.Launcher.Cli[.exe] launch --adapter raylib --build never mod:MyMod ...`
- 约定：玩家默认拿预编译包；BuildableSource 现场编译仅保留给开发者/调试（`--build auto`）。

## 3 边界与后续

- 发行脚本当前只打 Raylib 平台；Web 平台仍依赖 Node（开发向）。
- 作者弱网一键见 [零环境/零网络契约](zero-env-setup.md) 的 `dev-up`；可选无 SDK 源码编译见同页族 E。
