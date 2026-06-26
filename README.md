# Ludots

**SuperFastECSGameplayFramework** - A high-performance, data-oriented gameplay framework built on Arch ECS.

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

[中文文档 (Chinese)](README_CN.md)

## 🌟 Introduction

Ludots is a modern C# game framework designed for high-performance gameplay logic. It leverages ECS (Entity Component System) architecture, deterministic simulation, and a modular design to support complex game genres like MOBA, RTS, and Simulation games.

## ✨ Key Features

*   **High-Performance ECS**: Built on [Arch](https://github.com/genaray/Arch), optimized for speed and memory efficiency.
*   **Gameplay Ability System (GAS)**: A robust ability system inspired by UE GAS, supporting attributes, effects, and tags.
*   **Modular Architecture**: Fully moddable with a Virtual File System (VFS) and hot-reloadable configurations.
*   **Advanced Navigation**: Integrated NavMesh, MassFlow movement, and local avoidance (ORCA).
*   **Deterministic Simulation**: Fixed-point math and deterministic scheduling for reliable networking and replay.
*   **Visual Editor**: React-based visual editor for map editing and debugging.

## 🚀 Quick Start

### Prerequisites
*   .NET 8.0 SDK or later
*   Node.js & npm (for Editor)

### Build & Run

**Using Convenience Scripts (Recommended)**

Scripts are located in the `scripts/` directory:

```bash
# Run the Visual Editor (Web + Bridge)
.\scripts\run-editor.cmd

# Run the Mod Launcher
.\scripts\run-mod-launcher.cmd
```

**Manual Build (CLI)**

```bash
# Build the main Raylib App
dotnet build .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release

# Run the Mass Navigation showcase through the launcher
.\scripts\run-mod-launcher.cmd preset mass_navigation
```

## 📂 Project Structure

*   `src/Core/`: The heart of the engine (ECS, GAS, Physics, Math).
*   `src/Apps/`: Application entry points (Desktop/Raylib, Web).
*   `mods/`: 30+ built-in and demo mods (outside `src/` for UGC parity).
*   `src/Tools/`: Developer tools (Editor, ModLauncher, NavBake).
*   `src/Libraries/`: Source-integrated third-party (Arch, DotRecast, Raylib-cs).
*   `docs/`: Documentation (formal SSOT: conventions, architecture, reference, ADR, audits, RFCs).

## 📚 Documentation

*   **[GitBook 首页](gitbook/README.md)** — Ludots 正式文档真相与阅读入口
*   **[贡献与开发](gitbook/contributing/README.md)** — 编码标准、Feature 开发工作流、AI 辅助开发规范、环境配置、文档治理
*   [架构 (Architecture)](gitbook/architecture/README.md) — 当前正式架构总览
*   [参考资料 (Reference)](gitbook/reference/README.md) — CLI 与项目地图等正式操作资料
*   [Publishing & Access](gitbook/reference/publishing-and-access.md) — GitBook Git Sync 接入方式与 GitHub 访问入口
*   [仓库深度材料](docs/README.md) — ADR、审计、RFC 与长篇设计说明

## 🤝 Contributing

This project is licensed under the **AGPL-3.0 License**. This means if you use this code in a project that is distributed (including over a network), you must also open source your project under the same AGPL license.

## 📄 License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** - see the [LICENSE](LICENSE) file for details.

---

## 🏆 Acknowledgments & Third-Party Libraries

We gratefully acknowledge the following open-source projects that make Ludots possible.

### Core Dependencies

| Library | License | Usage & Modifications | Source |
| :--- | :--- | :--- | :--- |
| **Arch** | MIT | **Core ECS**. Integrated as source in `src/Libraries/Arch`. Critical high-performance ECS backend. | [genaray/Arch](https://github.com/genaray/Arch) |
| **Arch.Extended** | MIT | **ECS Utilities**. Source integrated. Provides additional ECS query and batching tools. | [genaray/Arch.Extended](https://github.com/genaray/Arch.Extended) |
| **DotRecast** | MIT | **Navigation**. Source integrated in `src/Libraries/DotRecast`. Used for NavMesh generation and pathfinding (Recast & Detour C# port). | [ikpil/DotRecast](https://github.com/ikpil/DotRecast) |
| **Raylib-cs** | Zlib | **Rendering**. Source integrated in `src/Libraries/Raylib-cs`. C# bindings for Raylib, used for the desktop client rendering. | [ChrisDill/Raylib-cs](https://github.com/ChrisDill/Raylib-cs) |
| **FixPointCS** | MIT | **Math**. Source integrated in `external/FixPointCS-master`. Deterministic fixed-point mathematics for simulation consistency. | [asik/FixPointCS](https://github.com/asik/FixPointCS) |

*Disclaimer: All trademarks and registered trademarks are the property of their respective owners.*

