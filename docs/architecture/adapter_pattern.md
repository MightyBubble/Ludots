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

## 5 Core 掌控范围与 Adapter 最小职责

**Core 层完全掌控**：相机逻辑、视口公式、同屏实体数量、WorldToScreen 投影、HUD 屏幕裁切。所有相关数学与逻辑均在 Core 实现，与平台无关。

**Adapter 层最小职责**：

| 接口 | Adapter 职责 |
|------|--------------|
| `IViewController` | 提供 `Resolution`、`AspectRatio`（实时从窗口读取） |
| `ICameraAdapter` | 接收 `CameraRenderState3D`，应用到平台相机 |
| `IScreenProjector` | 由 Core 的 `CoreScreenProjector` 实现，Adapter 不提供 |
| HUD 绘制 | 仅遍历 `ScreenHudBatchBuffer` 绘制，无投影、无裁切 |

**分辨率**：统一通过 `IViewController.Resolution` 获取，Adapter 在窗口 resize 时更新 UI/Skia 等。

## 6 UI 渲染适配

UI 体系同样遵循六边形架构原则。`Ludots.UI` 程序集零平台依赖，通过策略接口隔离渲染后端：

| 接口（Port） | 位置 | Skia 实现（Adapter） | 位置 |
|---------------|------|---------------------|------|
| `IUiTextMeasurer` | `Ludots.UI/Runtime/` | `SkiaTextMeasurer` | `Ludots.UI.Skia/` |
| `IUiImageSizeProvider` | `Ludots.UI/Runtime/` | `SkiaImageSizeProvider` | `Ludots.UI.Skia/` |
| `IUiRenderer` | `Ludots.UI/Runtime/` | `SkiaUiRenderer` | `Ludots.UI.Skia/` |
| `IUiCanvasContent` | `Ludots.UI/Runtime/` | `UiCanvasContent` | `Ludots.UI.Skia/` |

依赖方向：

```
Ludots.UI              → 零渲染依赖
Ludots.UI.Skia         → Ludots.UI + SkiaSharp + Svg.Skia
Adapter (Raylib/Web)   → 组装 Skia 实现并注入 UIRoot / UiScene
```

`Ludots.UI` 使用 `UiColor`（平台无关 RGBA struct）替代 `SKColor`，使用 `System.Numerics.Matrix3x2` 替代 `SKMatrix`。边界转换由 `Ludots.UI.Skia/UiSkiaExtensions` 提供（`ToSKColor()`、`ToUiColor()`、`ToSKMatrix()`）。

详见 `docs/architecture/ui_runtime_architecture.md`。

## 7 UE5 Host-Bound Map Session Contract

UE5 adapter 内与宿主 world / level ownership 相关的状态，必须遵守单一真相来源：

- focused Ludots map 以 `GameEngine.CurrentMapSession` / `MapSessionManager.FocusedSession` 为准
- 是否进入 host-bound 路径，只能由 adapter-level 的显式 map binding resolver 决定
- `HasPendingReturn` 必须直接复用 `MapSessionManager` 的正式嵌套 map 语义，不得在 adapter 里另造一套 push/pop bookkeeping
- `MapLoaded` 不是“开始切 host world”，而是“Ludots map 完成加载”；只要 host world 或其必需 host-bound entities 还没 ready，`MapLoadStatus` 就必须保持 pending，`MapLoaded` 不能提前触发
- host adapter 可以观察 `MapLoadStatus`，但不能再维护平行的 “host-ready” 真相来源
- 产品 UI 只能观察 `HostBoundMapSessionSnapshot`，不得接管生命周期 bookkeeping

这层 contract 只允许放在 `src/Adapters/UE5/Ludots.Adapter.UE5/`，不能把 menu、preview、scenario 选择等产品逻辑编码进 adapter 基建。
