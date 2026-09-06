# Ludots

**SuperFastECSGameplayFramework** - A high-performance, data-oriented gameplay framework built on Arch ECS.

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE)

[中文文档 (Chinese)](README_CN.md)

## 📖 Documentation Portal

**Official entry: [https://mightybubble.github.io/Ludots/](https://mightybubble.github.io/Ludots/)** — documentation, Showcase gallery, test acceptance evidence, and the architecture diagram library, all aggregated in one place. New here? Start from the portal.

## 🌟 Introduction

Ludots is a modern C# game framework designed for high-performance gameplay logic. It leverages ECS (Entity Component System) architecture, deterministic simulation, and a modular design to support complex game genres like MOBA, RTS, and Simulation games.

## ✨ Key Features

*   **High-Performance ECS**: Built on [Arch](https://github.com/genaray/Arch), optimized for speed and memory efficiency.
*   **Gameplay Ability System (GAS)**: A robust ability system inspired by UE GAS, supporting attributes, effects, and tags.
*   **Modular Architecture**: Fully moddable with a Virtual File System (VFS) and hot-reloadable configurations.
*   **Advanced Navigation**: Integrated NavMesh, MassNavigationFlow movement, and local avoidance (ORCA).
*   **Deterministic Simulation**: Fixed-point math and deterministic scheduling for reliable networking and replay.
*   **Visual Editor**: React-based visual editor for map editing and debugging.

## 🚀 Quick Start

> The commands below are the canonical way to build and run. For the full newcomer guide (environment, showcase tour, acceptance evidence), see the [Documentation Portal](https://mightybubble.github.io/Ludots/).

### Prerequisites
*   .NET 9.0 SDK (only hard prerequisite)
*   In-repo offline NuGet (`external/nuget/`): canonical paths work offline — no nuget.org hunt
*   Node.js & npm (editor / Web only; Raylib CLI one-shot path does not need them)

The whole repo targets `net9.0` (`global.json` pins SDK 9.0.x). Weak-network contract: [Quick Start](gitbook/quick-start.md) / [zero-env setup](gitbook/reference/zero-env-setup.md).

### Build & Run

**Weak-network one-shot (recommended, Linux / macOS / Windows)**

```bash
./scripts/dev-up.sh          # Linux/macOS: offline restore + build + launch ExampleMod
```

```powershell
.\scripts\dev-up.ps1         # Windows
```

**Other convenience scripts**

Scripts are located in the `scripts/` directory:

```bash
# Run the Visual Editor (Web + Bridge)
.\scripts\run-editor.cmd

# Run the Mod Launcher
.\scripts\run-mod-launcher.cmd
```

**Manual Build (CLI)**

```bash
# Run the Mass Navigation showcase through the launcher (canonical product entry)
.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib
```

Building the adapter app directly is for debugging only — it is not the product entry:

```bash
# Debug use only: build the Raylib adapter app
dotnet build .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release
```

## 📂 Project Structure

*   `src/Core/`: The heart of the engine (ECS, GAS, Physics, Math).
*   `src/Apps/`: Application entry points (Desktop/Raylib, Web).
*   `mods/`: 30+ built-in and demo mods (outside `src/` for UGC parity).
*   `src/Tools/`: Developer tools (Launcher CLI, Editor.Bridge, AgentBridge, NavBake).
*   `src/Libraries/`: Source-integrated third-party (Arch, DotRecast, Raylib-cs).
*   `docs/`: Portal site source and in-repo deep materials (conventions, architecture, reference, ADR, audits, RFCs).

## 📚 Documentation

The single official entry is the **[Ludots Documentation Portal](https://mightybubble.github.io/Ludots/)** — documentation, Showcase gallery, test acceptance evidence, and the architecture diagram library are all published there.

*   Writing source (markdown): [`gitbook/`](gitbook/README.md) — navigation in `gitbook/SUMMARY.md`, assembled into the portal by CI
*   [Contributing & Development](gitbook/contributing/README.md) — coding standards, feature development workflow, AI-assisted development rules, environment setup
*   Showcase & acceptance registry: `showcase.registry.json` (repo root)
*   [In-repo deep materials](docs/README.md) — portal site source, ADR, audits, RFCs, and long-form design docs
*   [pi 自动化编排工作流](docs/orchestration.md) — 维护者在 issue 上打 `pi:auto` 标签，conductor 在隔离 worktree 拉起 pi agent 实现并开 PR

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

