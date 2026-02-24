# Ludots

**SuperFastECSGameplayFramework** - A high-performance, data-oriented gameplay framework built on Arch ECS.

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

## 🌟 Introduction (简介)

Ludots is a modern C# game framework designed for high-performance gameplay logic. It leverages ECS (Entity Component System) architecture, deterministic simulation, and a modular design to support complex game genres like MOBA, RTS, and Simulation games.

Ludots 是一个现代化的 C# 游戏框架，专为高性能游戏逻辑设计。它利用 ECS（实体组件系统）架构、确定性模拟和模块化设计，支持 MOBA、RTS 和模拟游戏等复杂游戏类型。

## ✨ Key Features (核心特性)

*   **High-Performance ECS**: Built on [Arch](https://github.com/genaray/Arch), optimized for speed and memory efficiency.
*   **Gameplay Ability System (GAS)**: A robust ability system inspired by UE GAS, supporting attributes, effects, and tags.
*   **Modular Architecture**: Fully moddable with a Virtual File System (VFS) and hot-reloadable configurations.
*   **Advanced Navigation**: Integrated 2D navigation with NavMesh, FlowField, and local avoidance (ORCA).
*   **Deterministic Simulation**: Fixed-point math and deterministic scheduling for reliable networking and replay.
*   **Visual Editor**: React-based visual editor for map editing and debugging.

## 🚀 Quick Start (快速开始)

### Prerequisites (前置要求)
*   .NET 8.0 SDK or later
*   Node.js & npm (for Editor)

### Build & Run (构建与运行)

**Using Convenience Scripts (Recommended) / 使用脚本（推荐）**

Scripts are located in the `scripts/` directory:

```bash
# Run the Visual Editor (Web + Bridge)
.\scripts\run-editor.cmd

# Run the Mod Launcher
.\scripts\run-mod-launcher.cmd
```

**Manual Build (CLI) / 手动构建**

```bash
# Build the main Raylib App
dotnet build .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release

# Run Navigation2D Playground
dotnet run --project .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json
```

## 📂 Project Structure (项目结构)

*   `src/Core`: The heart of the engine (ECS, GAS, Physics, Math).
*   `src/Apps`: Application entry points (Desktop/Raylib, Web).
*   `src/Mods`: Built-in mods and examples (MobaDemo, RtsDemo).
*   `src/Tools`: Developer tools (Editor, ModLauncher, NavBake).
*   `assets`: Game assets and configurations.
*   `docs`: Comprehensive documentation.

## 📚 Documentation (文档)

Detailed documentation can be found in the `docs/` directory. (Note: Most documentation is currently internal/private, only Architecture Guidelines are public).

详细文档位于 `docs/` 目录中。（注：大部分文档目前为内部/私有，仅架构指南公开）。

*   [Architecture Guide (架构指南)](docs/arch-guide/README.md)

## 🤝 Contributing (贡献)

This project is licensed under the **AGPL-3.0 License**. This means if you use this code in a project that is distributed (including over a network), you must also open source your project under the same AGPL license.

本项目采用 **AGPL-3.0 许可证**。这意味着如果您在分发（包括通过网络分发）的项目中使用了此代码，您也必须在相同的 AGPL 许可证下开源您的项目。

## 📄 License (许可证)

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** - see the [LICENSE](LICENSE) file for details.

本项目基于 **GNU Affero General Public License v3.0 (AGPL-3.0)** 授权 - 详情请参阅 [LICENSE](LICENSE) 文件。

---

## 🏆 Acknowledgments & Third-Party Libraries (致谢与第三方库)

We gratefully acknowledge the following open-source projects that make Ludots possible.
我们衷心感谢以下开源项目，它们是 Ludots 的重要基石。

### Core Dependencies (核心依赖)

| Library | License | Usage & Modifications (用途与修改) | Source |
| :--- | :--- | :--- | :--- |
| **Arch** | MIT | **Core ECS**. Integrated as source in `src/Libraries/Arch`. Critical high-performance ECS backend. | [genaray/Arch](https://github.com/genaray/Arch) |
| **Arch.Extended** | MIT | **ECS Utilities**. Source integrated. Provides additional ECS query and batching tools. | [genaray/Arch.Extended](https://github.com/genaray/Arch.Extended) |
| **DotRecast** | MIT | **Navigation**. Source integrated in `src/Libraries/DotRecast`. Used for NavMesh generation and pathfinding (Recast & Detour C# port). | [ikpil/DotRecast](https://github.com/ikpil/DotRecast) |
| **Raylib-cs** | Zlib | **Rendering**. Source integrated in `src/Libraries/Raylib-cs`. C# bindings for Raylib, used for the desktop client rendering. | [ChrisDill/Raylib-cs](https://github.com/ChrisDill/Raylib-cs) |
| **FixPointCS** | MIT | **Math**. Source integrated in `external/FixPointCS-master`. Deterministic fixed-point mathematics for simulation consistency. | [asik/FixPointCS](https://github.com/asik/FixPointCS) |

### Tools & Web Frontend (工具与前端)

| Library | License | Usage (用途) |
| :--- | :--- | :--- |
| **React** | MIT | Web Editor UI framework. |
| **Three.js** | MIT | 3D visualization in the Web Editor. |
| **Vite** | MIT | Frontend build tool. |
| **Zustand** | MIT | State management for the editor. |
| **Radix UI** | MIT | Accessible UI primitives. |
| **TailwindCSS** | MIT | Utility-first CSS framework. |

*Disclaimer: All trademarks and registered trademarks are the property of their respective owners.*
