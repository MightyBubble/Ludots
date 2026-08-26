# 零环境 / 零网络契约

本页定义 epic #1190 的第一性验收：**作者弱网也能把仓库拉起来跑，不必上网找 NuGet 包**（.NET 9 SDK 仍需自备）。

## 1 依赖分层

| 层 | 内容 | 位置 | 网络 |
|---|---|---|---|
| 作者一键入口 | `scripts/dev-up.sh` / `scripts/dev-up.ps1` | 离线还原 + 构建 + Raylib 启动 | 无（规范路径） |
| 玩家发行包 | 自包含 App + Launcher + BinaryOnly mods | `scripts/publish-player-build.ps1` 产出 | 无（见 [玩家发行链](player-build.md)） |
| 框架引用程序集 | net9.0 ref pack（~164 dll） | `external/ref/net9.0/` | 无 |
| 离线 NuGet 源 | 规范闭包 nupkg（含 win/linux/osx 自包含运行时包） | `external/nuget/` | 无 |
| .NET 9 SDK | 编译仓库源码本身 | 用户自备（唯一前置） | 无 |

根 `nuget.config` 以 `<clear/>` + `LudotsOffline` 本地源接管全部还原：canonical 路径（Core / Raylib / Launcher / 全部维护测试 / mods）**完全离线**。Cef 与 Blazor WASM 特例（Chromium natives ~230MB 不入库）在其各自目录（`src/Libraries/Ludots.UI.Browser.Cef`、`src/Tests/BrowserCefTests`、`src/Platforms/Web`）有附加 nuget.config 显式引入 nuget.org——只在首次构建这些特例时联网，之后走包缓存。

验证口径：`HTTPS_PROXY` 指向死端口 + 干净 `NUGET_PACKAGES` 下，GasTests / Launcher.Cli / App.Raylib / App.Web / mods 的 `dotnet restore` 全绿即视为离线成立；`./scripts/dev-up.sh build-only` 在 Linux/macOS agent 上应同样绿。

## 2 跨平台

| 平台 | 作者入口 | 玩家包 RID 默认 |
|---|---|---|
| Windows | `scripts/dev-up.ps1` | `win-x64`（`Play.cmd`） |
| Linux | `scripts/dev-up.sh` | `linux-x64`（`Play.sh`） |
| macOS | `scripts/dev-up.sh` | `osx-x64` / `osx-arm64`（`Play.sh`） |

启动器识别自包含 apphost：Windows 为 `*.exe`，Unix 为与 DLL 同名的无扩展名文件；开发机布局仍可用 `dotnet exec`（无 apphost 且无 dotnet 时显式失败）。

## 3 可选：无 SDK 的 mod 编译（族E）

```powershell
.\scripts\compile-mod.ps1 -ModDir mods\ExampleMod
```

- 内嵌 Roslyn（`src/Tools/Ludots.ModCompiler`）进程内编译，产出等价 `bin/net9.0/*.dll`。
- 引用解析：`external/ref/net9.0` → `assets/ModSdk/ref`（launcher 导出）→ 依赖 mod bin → 显式 `-r`。
- 依赖闭包需先编译依赖（脚本不自动递归）。
- 日常作者主路径不依赖此工具；弱网一键以 `dev-up` + 离线 NuGet 为准。

## 4 维护契约

- **增删 PackageReference**：把新包 nupkg 放入 `external/nuget/`（连同依赖闭包），canonical 路径保持离线；否则还原会因 `<clear/>` 失败——这是有意的防回归信号。
- **TFM 升级**：同步 `external/ref/<tfm>/`（从 SDK packs 提取）与 `mods/Directory.Build.props` 字面量。
- **ModSdk 引用集**：`LauncherModSdkExporter.ProjectSpecs` 是单一来源。
- npm（Web 客户端 / Launcher UI）仍是开发向依赖，不在离线契约内；玩家路径与 `dev-up` Raylib 路径不触碰。
