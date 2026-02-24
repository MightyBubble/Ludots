# Build Instructions

Since the current environment lacks the .NET 9 SDK, please run the following command in your local environment to start the project:

```bash
dotnet run --project src/Platforms/Web/Ludots.Web.csproj
```

## MVP Features Implemented
- **Core Architecture**: ECS (Arch), Pure Integer Logic (Ludots.Core), Player/Input Abstraction.
- **Procedural Map**: 256x256 Terrain Generation using SimpleNoise.
- **Web Rendering**: Three.js InstancedMesh for Terrain (65k tiles) and Entities.
- **Resource Loading**: JSON Configs -> ECS Entities -> Visuals.
