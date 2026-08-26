# 零环境 / 零网络契约

本页定义 epic #1190 的第一性验收：**新用户拿到仓库（或发行包）后，0 环境配置、0 外部包网络依赖**。

## 1 依赖分层

| 层 | 内容 | 位置 | 网络 |
|---|---|---|---|
| 玩家发行包 | 自包含 App + Launcher + BinaryOnly mods | `scripts/publish-player-build.ps1` 产出 | 无（见 [玩家发行链](player-build.md)） |
| 框架引用程序集 | net9.0 ref pack（164 dll，~6MB） | `external/ref/net9.0/` | 无 |
| 离线 NuGet 源 | 规范闭包 157 个 nupkg（~178MB） | `external/nuget/` | 无 |
| .NET 9 SDK | 编译仓库源码本身 | 用户自备（唯一前置） | 无 |

根 `nuget.config` 以 `<clear/>` + `LudotsOffline` 本地源接管全部还原：canonical 路径（Core / Raylib / Launcher / 全部维护测试 / mods）**完全离线**。Cef 与 Blazor WASM 特例（Chromium natives ~230MB 不入库）在其各自目录（`src/Libraries/Ludots.UI.Browser.Cef`、`src/Tests/BrowserCefTests`、`src/Platforms/Web`）有附加 nuget.config 显式引入 nuget.org——只在首次构建这些特例时联网，之后走包缓存。

验证口径：`HTTPS_PROXY` 指向死端口 + 干净 `NUGET_PACKAGES` 下，GasTests / Launcher.Cli / App.Raylib / App.Web / mods 的 `dotnet restore` 全绿即视为离线成立。

## 2 无 SDK 的 mod 编译（族E 根治）

```powershell
.\scripts\compile-mod.ps1 -ModDir mods\ExampleMod
```

- 内嵌 Roslyn（`src/Tools/Ludots.ModCompiler`）进程内编译，产出等价 `bin/net9.0/*.dll`；对齐 mods csproj 默认（ImplicitUsings + Nullable + AllowUnsafe + Release + deterministic）。
- 引用解析：`external/ref/net9.0`（框架）→ `assets/ModSdk/ref`（Ludots/Arch SDK，launcher build 副产品，脚本缺失时自动导出）→ 各依赖 mod 的 `bin/net9.0` → 显式 `-r`。
- 依赖闭包需先编译依赖（脚本不自动递归）。
- 已验证：LudotsCoreMod / CoreInputMod / ExampleMod 经此链编译后在真实游戏内加载运行。

## 3 维护契约

- **增删 PackageReference**：把新包 nupkg 放入 `external/nuget/`（连同依赖闭包），canonical 路径保持离线；否则还原会因 `<clear/>` 失败——这是有意的防回归信号。
- **TFM 升级**：同步 `external/ref/<tfm>/`（从 SDK packs 提取）与 `mods/Directory.Build.props` 字面量。
- **ModSdk 引用集**：`LauncherModSdkExporter.ProjectSpecs` 是单一来源；mod 编译报「类型在未引用程序集中定义」时把对应工程加进清单。
- npm（Web 客户端 / Launcher UI）仍是开发向依赖，不在离线契约内；玩家路径不触碰。
