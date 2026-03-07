# 适配器模式与平台抽象

Ludots 采用六边形架构（Hexagonal Architecture）思想，将核心逻辑（Core）与具体平台实现（App / Adapter / Client）完全解耦。这使得游戏核心可以在不依赖任何特定图形库、窗口系统或浏览器运行时的情况下运行和测试。

## 1 架构分层

*   **Core (核心层)**: 包含纯 C# 逻辑、ECS 系统、数据组件。不依赖任何外部图形/输入库，仅依赖抽象接口。
*   **Ports (接口层)**: 定义核心层所需的外部服务接口，例如 `IInputBackend`、`IViewController`、`ICameraAdapter`、`IScreenRayProvider`。
*   **Adapters (适配器层)**: 负责把平台输入、视口、相机、传输等能力接入 Core。
*   **Clients (客户端层)**: 负责平台专属渲染与本地交互实现；桌面端是 Raylib 渲染器，Web 端是浏览器 Three.js 客户端。
*   **Apps (入口层)**: 负责进程入口、命令行参数、Host 创建与启动顺序。

## 2 核心抽象接口

### 2.1 输入抽象

位于 `src/Core/Input/Runtime/IInputBackend.cs`。

核心层通过此接口轮询输入状态，而不直接调用平台 API。

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

核心层不直接调用绘制 API，而是产出平台无关的表现数据。

*   **PrimitiveDrawBuffer**: 用于调试绘制（线、圆、框）。Adapter/Client 在渲染阶段读取并消费。
*   **GroundOverlayBuffer**: 地面投影覆盖层（圈、线、环等）。
*   **ScreenHudBatchBuffer / ScreenOverlayBuffer**: 屏幕 HUD 与屏幕覆盖层。
*   **Visual / Performer / CameraRenderState3D**: Core 掌控的表现与相机状态，同步给平台实现。

## 3 Raylib 实现导航

Raylib 平台采用清晰的 `App -> Adapter -> Client` 分层：

1.  **App**：`src/Apps/Raylib/Ludots.App.Raylib/Program.cs` 解析参数并启动 Host。
2.  **Adapter**：`src/Adapters/Raylib/Ludots.Adapter.Raylib/` 负责组装引擎、注入 `IInputBackend` / `IViewController` / `ICameraAdapter` / `IScreenRayProvider`、驱动主循环。
3.  **Client**：`src/Client/Ludots.Client.Raylib/` 提供 Raylib 输入后端与具体渲染器，例如 `RaylibInputBackend`、`RaylibPrimitiveRenderer`、`RaylibTerrainRenderer`。

### 代码结构

*   `src/Apps/Raylib/Ludots.App.Raylib`: 桌面入口
*   `src/Adapters/Raylib/Ludots.Adapter.Raylib`: Host、平台服务与 UI 系统
*   `src/Client/Ludots.Client.Raylib`: 输入后端、渲染器实现

## 4 Web 实现导航

Web 主线同样采用 `App -> Adapter -> Client` 分层，且与 Raylib 保持相同职责边界：

1.  **App**：`src/Apps/Web/Ludots.App.Web/Program.cs` 创建 ASP.NET Core 入口，启动 `WebGameHost`，暴露 `/ws` 与静态资源。
2.  **Adapter**：`src/Adapters/Web/Ludots.Adapter.Web/` 负责组装引擎、注入 `WebInputBackend`、`WebViewController`、`WebCameraAdapter`、`WebScreenRayProvider`，并在 `WebHostLoop` 中驱动 `engine.Tick(dt)`、提取表现帧、通过 `WebTransportLayer` 广播。
3.  **Client**：`src/Client/Web/` 是浏览器端客户端；`src/Client/Web/src/main.ts` 负责 WebSocket 连接、输入采集、帧解码与 Three.js / Canvas 渲染。

### 代码结构

*   `src/Apps/Web/Ludots.App.Web`: Web 服务器入口
*   `src/Adapters/Web/Ludots.Adapter.Web`: Host、平台服务、帧提取与传输协议
*   `src/Client/Web`: 浏览器客户端源码与前端构建配置

## 5 适配器原则

1.  **单向依赖**: App / Adapter / Client 依赖 Core，Core **绝不** 依赖平台实现。
2.  **最小接口**: 仅暴露 Core 运行所需的最小功能集，不在 Adapter 中复制 Core 逻辑。
3.  **数据转换**: Adapter 负责平台数据与 Core 通用数据之间的转换。
4.  **唯一运行栈**: 同一平台只保留一套主线 App / Adapter / Client，不保留平行旧栈或临时 fallback。

## 6 Core 掌控范围与 Adapter 最小职责

**Core 层完全掌控**：相机逻辑、视口公式、同屏实体数量、WorldToScreen 投影、HUD 屏幕裁切、表现缓冲区结构。所有相关数学与逻辑均在 Core 实现，与平台无关。

**Adapter 层最小职责**：

| 接口 | Adapter 职责 |
|------|--------------|
| `IInputBackend` | 提供统一输入状态读取 |
| `IViewController` | 提供 `Resolution`、`AspectRatio` |
| `ICameraAdapter` | 接收 `CameraRenderState3D` 并应用到平台相机 |
| `IScreenRayProvider` | 提供屏幕点到世界射线 |
| Web 传输层 | 仅传输 Core 已产出的帧与输入协议，不引入第二套表现真相源 |
| Client 渲染层 | 仅消费 Core/Adapter 下发的数据，不重建业务逻辑 |

**分辨率**：统一通过 `IViewController.Resolution` 获取；Web 在收到浏览器视口输入后更新，Raylib 则从运行窗口实时读取。
