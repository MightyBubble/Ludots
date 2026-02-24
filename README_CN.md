# Ludots

**SuperFastECSGameplayFramework** - 基于 Arch ECS 构建的高性能、数据导向的游戏逻辑框架。

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

[English Documentation](README.md)

## 🌟 简介

Ludots 是一个现代化的 C# 游戏框架，专为高性能游戏逻辑设计。它利用 ECS（实体组件系统）架构、确定性模拟和模块化设计，支持 MOBA、RTS 和模拟游戏等复杂游戏类型。

## ✨ 核心特性

*   **高性能 ECS**: 基于 [Arch](https://github.com/genaray/Arch) 构建，针对速度和内存效率进行了优化。
*   **Gameplay Ability System (GAS)**: 受 UE GAS 启发的强大能力系统，支持属性、效果和标签。
*   **模块化架构**: 完全支持 Mod，拥有虚拟文件系统 (VFS) 和支持热重载的配置。
*   **高级导航**: 集成 NavMesh、流场 (FlowField) 和局部避障 (ORCA) 的 2D 导航系统。
*   **确定性模拟**: 使用定点数数学库和确定性调度，确保可靠的网络同步和回放。
*   **可视化编辑器**: 基于 React 的可视化编辑器，用于地图编辑和调试。

## 🚀 快速开始

### 前置要求
*   .NET 8.0 SDK 或更高版本
*   Node.js & npm (用于编辑器)

### 构建与运行

**使用脚本（推荐）**

脚本位于 `scripts/` 目录下：

```bash
# 运行可视化编辑器 (Web + Bridge)
.\scripts\run-editor.cmd

# 运行 Mod 启动器
.\scripts\run-mod-launcher.cmd
```

**手动构建 (CLI)**

```bash
# 构建主 Raylib 应用程序
dotnet build .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release

# 运行 Navigation2D 演示
dotnet run --project .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json
```

## 📂 项目结构

*   `src/Core`: 引擎核心 (ECS, GAS, Physics, Math)。
*   `src/Apps`: 应用程序入口 (Desktop/Raylib, Web)。
*   `src/Mods`: 内置 Mod 和示例 (MobaDemo, RtsDemo)。
*   `src/Tools`: 开发者工具 (Editor, ModLauncher, NavBake)。
*   `assets`: 游戏资源和配置。
*   `docs`: 详细文档。

## 📚 文档

详细文档位于 `docs/` 目录中。

*   [架构指南](docs/arch-guide/README.md)

## 🤝 贡献

本项目采用 **AGPL-3.0 许可证**。这意味着如果您在分发（包括通过网络分发）的项目中使用了此代码，您也必须在相同的 AGPL 许可证下开源您的项目。

## 📄 许可证

本项目基于 **GNU Affero General Public License v3.0 (AGPL-3.0)** 授权 - 详情请参阅 [LICENSE](LICENSE) 文件。

---

## 🏆 致谢与第三方库

我们衷心感谢以下开源项目，它们是 Ludots 的重要基石。

### 核心依赖

| 库 | 许可证 | 用途与修改 | 来源 |
| :--- | :--- | :--- | :--- |
| **Arch** | MIT | **核心 ECS**。以源码形式集成在 `src/Libraries/Arch`。关键的高性能 ECS 后端。 | [genaray/Arch](https://github.com/genaray/Arch) |
| **Arch.Extended** | MIT | **ECS 工具集**。以源码形式集成。提供额外的 ECS 查询和批处理工具。 | [genaray/Arch.Extended](https://github.com/genaray/Arch.Extended) |
| **DotRecast** | MIT | **导航系统**。以源码形式集成在 `src/Libraries/DotRecast`。用于 NavMesh 生成和寻路 (Recast & Detour C# 移植版)。 | [ikpil/DotRecast](https://github.com/ikpil/DotRecast) |
| **Raylib-cs** | Zlib | **渲染**。以源码形式集成在 `src/Libraries/Raylib-cs`。Raylib 的 C# 绑定，用于桌面客户端渲染。 | [ChrisDill/Raylib-cs](https://github.com/ChrisDill/Raylib-cs) |
| **FixPointCS** | MIT | **数学库**。以源码形式集成在 `external/FixPointCS-master`。用于模拟一致性的确定性定点数数学库。 | [asik/FixPointCS](https://github.com/asik/FixPointCS) |

*免责声明：所有商标和注册商标均为其各自所有者的财产。*
