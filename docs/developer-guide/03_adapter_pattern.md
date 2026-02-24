# 适配器模式与平台抽象

Ludots 采用六边形架构（Hexagonal Architecture）思想，将核心逻辑（Core）与具体平台实现（Client/Adapter）完全解耦。这使得游戏核心可以在不依赖任何特定游戏引擎（如 Unity、Godot）或图形库（如 Raylib）的情况下运行和测试。

## 1 架构分层

*   **Core (核心层)**: 包含纯 C# 逻辑、ECS 系统、数据组件。不依赖任何外部图形/输入库，仅依赖抽象接口。
*   **Ports (接口层)**: 定义了核心层所需的外部服务接口（如 `IInputBackend`, `IRenderBackend`）。
*   **Adapters (适配器层)**: 针对特定平台的接口实现（如 `RaylibInputBackend`, `UnityRenderAdapter`）。

## 2 核心抽象接口

### 2.1 输入抽象

位于 `src/Core/Input/Runtime/IInputBackend.cs`。

核心层通过此接口轮询输入状态，而不直接调用 `Input.GetKey()`。

```csharp
using System.Numerics;

public interface IInputBackend
{
    float GetAxis(string devicePath);
    bool GetButton(string devicePath);
    Vector2 GetMousePosition();
    float GetMouseWheel();

    void EnableIME(bool enable);
    void SetIMECandidatePosition(int x, int y);
    string GetCharBuffer();
}
```

### 2.2 渲染输出抽象

核心层不直接调用绘制 API。相反，它通过 ECS 系统生成**渲染指令**或**同步状态**。

*   **PrimitiveDrawBuffer**: 用于调试绘制（线、圆、框）。Core 系统将图元写入此缓冲，Adapter 在渲染阶段读取并绘制。
*   **VisualTransform**: Core 更新逻辑位置 (`WorldPositionCm`)，并同步到 `VisualTransform` 组件（包含平滑插值后的坐标）。Adapter 仅需渲染带有 `VisualTransform` 的实体。

## 3 Raylib 实现导航

以 Raylib 平台为例，适配器层主要包含以下部分：

1.  **Host**：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs` 驱动主循环；`RaylibHostComposer.cs` 负责组装依赖；`RaylibGameHost.cs` 负责平台侧初始化与生命周期。
2.  **输入适配**：`src/Client/Ludots.Client.Raylib/Input/RaylibInputBackend.cs` 实现 `IInputBackend`，把 Raylib 输入映射为统一的 `devicePath` 语义。
3.  **渲染实现**：`src/Client/Ludots.Client.Raylib/Rendering/` 下提供多个渲染器（如 `RaylibPrimitiveRenderer`、`RaylibTerrainRenderer`），消费 Core 输出的 draw buffers 与组件状态。

### 代码结构

*   `src/Adapters/Raylib/Ludots.Adapter.Raylib`: Host、平台服务与 UI 系统
*   `src/Client/Ludots.Client.Raylib`: 输入后端、渲染器实现

## 4 适配器原则

1.  **单向依赖**: Adapter 依赖 Core，Core **绝不** 依赖 Adapter。
2.  **最小接口**: 仅暴露 Core 运行所需的最小功能集。
3.  **数据转换**: Adapter 负责将平台特有的数据格式（如 `Vector3`, `Texture2D`）转换为 Core 通用的数据格式（如 `Fix64Vec2`, `ResourceHandle`）。
