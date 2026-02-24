# Ludots

**SuperFastECSGameplayFramework** - A high-performance, data-oriented gameplay framework built on Arch ECS.

## 🌟 Introduction

Ludots is a modern C# game framework designed for high-performance gameplay logic. It leverages ECS (Entity Component System) architecture, deterministic simulation, and a modular design to support complex game genres like MOBA, RTS, and Simulation games.

## ✨ Key Features

*   **High-Performance ECS**: Built on [Arch](https://github.com/genaray/Arch), optimized for speed and memory efficiency.
*   **Gameplay Ability System (GAS)**: A robust ability system inspired by UE GAS, supporting attributes, effects, and tags.
*   **Modular Architecture**: Fully moddable with a Virtual File System (VFS) and hot-reloadable configurations.
*   **Advanced Navigation**: Integrated 2D navigation with NavMesh, FlowField, and local avoidance (ORCA).
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

# Run Navigation2D Playground
dotnet run --project .\src\Apps\Raylib\Ludots.App.Raylib\Ludots.App.Raylib.csproj -c Release -- game.navigation2d.json
```

## 📂 Project Structure

*   `src/Core`: The heart of the engine (ECS, GAS, Physics, Math).
*   `src/Apps`: Application entry points (Desktop/Raylib, Web).
*   `src/Mods`: Built-in mods and examples (MobaDemo, RtsDemo).
*   `src/Tools`: Developer tools (Editor, ModLauncher, NavBake).
*   `assets`: Game assets and configurations.
*   `docs`: Comprehensive documentation.

## 📚 Documentation

Detailed documentation can be found in the `docs/` directory.

*   [Documentation Overview](docs/00_文档总览/00_README.md)
*   [Architecture Guide](docs/arch-guide/README.md)

## 🤝 Contributing

Contributions are welcome! Please check the `docs/` for coding standards and architectural guidelines.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
