# 适配器模式与平台抽象

Ludots 采用六边形架构（Hexagonal Architecture）思想，将核心逻辑（Core）与具体平台实现（Client/Adapter）完全解耦。这使得游戏核心可以在不依赖任何特定游戏引擎（如 Unity、Godot）或图形库（如 Raylib）的情况下运行和测试。

## 1. 架构分层

*   **Core (核心层)**: 包含纯 C# 逻辑、ECS 系统、数据组件。不依赖任何外部图形/输入库，仅依赖抽象接口。
*   **Ports (接口层)**: 定义了核心层所需的外部服务接口（如 `IInputBackend`, `IRenderBackend`）。
*   **Adapters (适配器层)**: 针对特定平台的接口实现（如 `RaylibInputBackend`, `UnityRenderAdapter`）。

## 2. 核心抽象接口

### 2.1 输入抽象 (IInputBackend)

位于 `src/Core/Input/Runtime/IInputBackend.cs`。

核心层通过此接口轮询输入状态，而不直接调用 `Input.GetKey()`。

```csharp
public interface IInputBackend
{
    // 获取轴向输入 (-1.0 to 1.0)
    float GetAxis(string axisName);
    
    // 获取按键状态
    bool GetButton(string buttonName);
    
    // 获取鼠标/指针位置（逻辑坐标）
    Fix64Vec2 GetPointerPosition();
}
```

### 2.2 渲染抽象 (Render Commands)

核心层不直接调用绘制 API。相反，它通过 ECS 系统生成**渲染指令**或**同步状态**。

*   **PrimitiveDrawBuffer**: 用于调试绘制（线、圆、框）。Core 系统将图元写入此缓冲，Adapter 在渲染阶段读取并绘制。
*   **VisualTransform**: Core 更新逻辑位置 (`WorldPositionCm`)，并同步到 `VisualTransform` 组件（包含平滑插值后的坐标）。Adapter 仅需渲染带有 `VisualTransform` 的实体。

## 3. Raylib 适配器实现示例

以 Raylib 平台为例，适配器层主要包含以下部分：

1.  **Host (宿主)**: `RaylibGameHost` 负责初始化 Raylib 窗口、主循环和资源加载。
2.  **Input Backend**: `RaylibInputBackend` 将 Raylib 的 `IsKeyDown` 映射到 Core 的 `GetButton`。
3.  **Render System**: `RaylibRenderSystem` 遍历所有带有 `VisualTransform` 和 `SpriteRendererCm` 的实体，调用 `Raylib.DrawTexture`。

### 代码结构 (src/Client/Ludots.Client.Raylib)

*   `Input/RaylibInputBackend.cs`: 实现输入接口。
*   `Rendering/RaylibRenderSystem.cs`: ECS 渲染系统（运行在 Core 之外的 Client World 中）。
*   `RaylibHostComposer.cs`: 组装 GameEngine，注入依赖。

## 4. 适配器原则

1.  **单向依赖**: Adapter 依赖 Core，Core **绝不** 依赖 Adapter。
2.  **最小接口**: 仅暴露 Core 运行所需的最小功能集。
3.  **数据转换**: Adapter 负责将平台特有的数据格式（如 `Vector3`, `Texture2D`）转换为 Core 通用的数据格式（如 `Fix64Vec2`, `ResourceHandle`）。
