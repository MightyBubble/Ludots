---
文档类型: 工具设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v1.0
适用范围: 工具链 - ModLauncher - CLI
状态: 已实现
---

# ModLauncher 工具设计

# 1 背景与目标

## 1.1 背景问题

Mod 开发与运行验证需要多个步骤：导出 Mod SDK、构建 Mod、生成 game.json、构建并运行 App。手动执行容易出错，需要统一工具支持。

## 1.2 设计目标

ModLauncher 是面向 Mod 开发与运行验证的启动器与 CLI：
- 统一完成 Mod SDK 导出、Mod 构建、生成运行用 game.json、构建并运行 Raylib App
- 支持 preset 配置快速切换不同 Mod 组合
- 自动处理 LudotsCoreMod 依赖

# 2 工具边界与非目标

## 2.1 工具边界

- 只负责构建与运行准备，不负责编辑地图或编译 Graph 资产
- 以 repo 根目录为基准工作（通过定位 `assets/` 自动发现 root）
- 自动扫描 `src/Mods/` 目录发现可用 Mods

## 2.2 非目标

- 不作为通用 build system；复杂流水线由 CI 或专用工具承担
- 不负责 Mod 资源处理（由 Ludots.Tool 承担）

# 3 输入输出与产物契约

## 3.1 输入

| 输入类型 | 说明 |
|---|---|
| Mod 目录 | 扫描 `src/Mods/`，可通过 config 扩展额外目录 |
| Preset | 从 config 读取或通过 `--preset` 指定，解析得到 ActiveModNames |
| 单独 Mod | 通过 `--mod` 或 `--mods` 指定 |

## 3.2 输出产物

| 产物 | 位置 | 说明 |
|---|---|---|
| Mod SDK | `assets/ModSdk/` | 供 Mod 工程引用的 SDK props |
| Mod 构建产物 | `{modPath}/bin/Release/` | Release 配置的 Mod DLL |
| App 构建产物 | `src/Apps/Raylib/.../bin/Release/net8.0/` | 可执行文件 |
| game.json | App 可执行文件同目录 | 运行时配置 |

## 3.3 运行时消费方式

`game.json` 被 `GameBootstrapper` 读取，通过 `ConfigPipeline` 与 Core 和各 Mod 的 `game.json` 合并。

**重要变更**：由于 Core 层解耦重构，所有 Mod 运行都必须依赖 `LudotsCoreMod`。`WriteGameJson` 命令会自动将 `LudotsCoreMod` 作为第一个 Mod 写入。

# 4 命令与参数

## 4.1 命令列表

| 命令 | 说明 |
|---|---|
| `sdk export` | 导出 Mod SDK 到 `assets/ModSdk/` |
| `mods build` | 构建指定 Mods（Release） |
| `app build` | 构建 Raylib App（Release） |
| `gamejson write` | 生成 game.json |
| `run` | 运行已构建的 App |

## 4.2 参数说明

| 参数 | 说明 |
|---|---|
| `--config <path>` | 指定配置文件路径（可选） |
| `--preset <presetId>` | 指定 preset（可选） |
| `--mod <nameOrPath>` | 指定单个 mod（可重复） |
| `--mods <a;b;c>` | 指定多个 mod（分号分隔） |

CLI 入口约定：第一个参数必须为 `cli`。

## 4.3 可复制示例

```bash
# 导出 Mod SDK
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli sdk export

# 构建 App
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli app build

# 构建指定 Mod
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli mods build --mod MobaDemoMod

# 生成 game.json（包含 MOBA Mod）
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --mod MobaDemoMod

# 运行 App
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run
```

**⚠️ 重要：Release 构建要求**

ModLauncher CLI 使用 **Release** 配置构建和运行。如果修改了 Core 或 Mod 代码，必须确保 Release 版本已更新：

```bash
# 完整的开发流程
# 1. 修改代码后，构建 Release 版本
dotnet build src/Core/Ludots.Core.csproj -c Release
dotnet build src/Mods/LudotsCoreMod/LudotsCoreMod.csproj -c Release
dotnet build src/Mods/MobaDemoMod/MobaDemoMod.csproj -c Release
dotnet build src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj -c Release

# 2. 生成 game.json 并运行
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli gamejson write --mod MobaDemoMod
dotnet run --project src/Tools/ModLauncher/Ludots.ModLauncher.csproj -c Release -- cli run
```

**常见问题**：如果 App 正在运行，DLL 会被锁定导致构建失败。需要先关闭 App：
```powershell
Stop-Process -Name "Ludots.App.Raylib" -Force -ErrorAction SilentlyContinue
```

**快捷启动方式（使用预设 JSON）**：

```powershell
# 复制 MOBA preset 并运行
Copy-Item "src/Apps/Raylib/Ludots.App.Raylib/game.moba.json" "src/Apps/Raylib/Ludots.App.Raylib/bin/Debug/net8.0/game.json" -Force
Start-Process "src/Apps/Raylib/Ludots.App.Raylib/bin/Debug/net8.0/Ludots.App.Raylib.exe"

# 复制 Physics2D preset 并运行
Copy-Item "src/Apps/Raylib/Ludots.App.Raylib/game.physics2d.json" "src/Apps/Raylib/Ludots.App.Raylib/bin/Debug/net8.0/game.json" -Force
Start-Process "src/Apps/Raylib/Ludots.App.Raylib/bin/Debug/net8.0/Ludots.App.Raylib.exe"
```

# 5 版本化与兼容性

## 5.1 版本号策略

- 默认配置文件位置：`%AppData%/Ludots/ModLauncher/config.json`
- preset 的语义与字段结构以 `ModLauncherConfig` 为准

## 5.2 破坏性变更与迁移方式

**重要变更（2026-02-05）**：
- Mod 扫描目录从 `assets/Mods/` 改为 `src/Mods/`
- `WriteGameJson` 自动添加 `LudotsCoreMod` 作为第一个依赖
- 预设 JSON 文件（`game.moba.json`、`game.physics2d.json`）必须包含 `LudotsCoreMod`

迁移方式：更新所有预设 JSON 的 `ModPaths` 列表，确保 `LudotsCoreMod` 在第一位。

# 6 失败策略与诊断信息

## 6.1 失败策略

| 场景 | 行为 |
|---|---|
| 找不到 repo root | fail-fast，错误信息：缺失 assets 目录 |
| 找不到 preset | fail-fast，错误信息：`Preset not found: {id}` |
| preset 引用缺失 mod | fail-fast，错误信息：`Preset refers to missing mod: {name}` |
| `--mod/--mods` 不存在 | fail-fast，错误信息：`Unknown mods: ...` |
| LudotsCoreMod 缺失 | fail-fast，错误信息指向 `src/Mods/LudotsCoreMod` |

## 6.2 诊断信息

错误信息格式统一，包含：
- 错误类型
- 受影响的资源路径
- 建议的解决方案

# 7 安全与破坏性操作

## 7.1 默认行为

| 操作 | 默认行为 |
|---|---|
| `mods build` | 覆盖 Release 输出目录中的既有产物 |
| `app build` | 覆盖 Release 输出目录中的既有产物 |
| `gamejson write` | **覆盖** App 目录下的 `game.json` |

## 7.2 保护开关与回滚方式

- 构建产物覆盖由 dotnet build 行为决定，无额外保护
- `gamejson write` 无备份机制，需手动备份
- 建议使用版本控制管理 game.json 预设文件

# 8 代码入口

## 8.1 命令入口

| 模块 | 路径 |
|---|---|
| CLI 入口 | `src/Tools/ModLauncher/App.xaml.cs` |
| 命令分发 | `src/Tools/ModLauncher/Cli/CliRunner.cs` |
| 参数解析 | `src/Tools/ModLauncher/Cli/CliArgs.cs` |

## 8.2 关键实现与测试

| 模块 | 路径 |
|---|---|
| 配置服务 | `src/Tools/ModLauncher/Config/LauncherConfigService.cs` |
| 配置模型 | `src/Tools/ModLauncher/Config/ModLauncherConfig.cs` |
| SDK 导出 | `src/Tools/ModLauncher/ModSdk/ModSdkExporter.cs` |

# 9 与 ConfigPipeline 的关系

ModLauncher 生成的 `game.json` 只包含 `ModPaths` 字段。运行时，`ConfigPipeline` 会：

1. 读取 App 层 `game.json`（ModLauncher 生成）
2. 读取 Core 层 `assets/Configs/game.json`（引擎默认参数）
3. 按 `ModPaths` 顺序读取各 Mod 的 `assets/game.json`
4. 深度合并生成 `MergedConfig`

**关键约束**：`LudotsCoreMod` 必须在 `ModPaths` 列表的第一位，因为它提供所有核心常量（`OrderTags`、`GasOrderTags`、`Attributes`）和默认系统。

# 10 预设文件

项目提供以下预设 JSON 文件，位于 `src/Apps/Raylib/Ludots.App.Raylib/`：

| 文件 | 用途 |
|---|---|
| `game.json` | 默认配置，包含所有 Mods |
| `game.moba.json` | MOBA Demo 测试 |
| `game.physics2d.json` | Physics2D Playground 测试 |

使用方式：复制预设文件到 `bin/Debug/net8.0/game.json` 后运行。