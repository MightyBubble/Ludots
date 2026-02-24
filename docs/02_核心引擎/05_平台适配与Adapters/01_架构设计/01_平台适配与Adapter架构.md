---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 核心引擎 - 平台适配与Adapters - 架构
状态: 草案
依赖文档:
  - docs/00_文档总览/01_文档规范/11_架构设计.md
  - docs/02_核心引擎/01_总架构/01_GameEngine_架构设计.md
---

# 平台适配与 Adapter 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 Ludots 的“平台适配层”整体架构：平台抽象、适配器、客户端实现、应用入口，以及它们如何装配 `GameEngine` 并驱动运行。

## 1.2 设计目标

1. Core 可移植：核心逻辑不依赖任何平台 SDK（Raylib/Unity/Web）。  
2. 注入显式：平台能力通过接口与 `GlobalContext` 显式注入，可追踪、可替换。  
3. 主循环归属清晰：平台 Host 管窗口/渲染/输入采样与 dt；Core 管逻辑推进与表现缓冲。  
4. fail-fast：启动期资源/配置/Mod 缺失必须中止，不做静默 fallback。  

## 1.3 设计思路

1. 把“最小平台契约”下沉到 `Ludots.Platform.Abstractions`。  
2. 把“平台实现绑定点”集中到 Adapter 层（把具体 SDK 粘到抽象上）。  
3. 把“对平台 SDK 的直接调用”集中到 Client 层（渲染器/输入实现）。  
4. 用 App 层选择 Host 并启动，把“平台选择”从 Core 中剥离。  

# 2 功能总览

## 2.1 术语表

| 术语 | 含义 |
|---|---|
| Platform.Abstractions | 平台最小契约（Host/投影/射线/渲染后端） |
| Adapter | 把 Core 的抽象与具体平台 SDK 对接（宿主/服务装配） |
| Client | 贴近平台 SDK 的实现件（渲染器/输入后端等） |
| App | 进程入口，选择 Host 并运行 |
| GlobalContext | 运行时注入容器（键由 ContextKeys 固化） |

## 2.2 分层与依赖方向

```
Apps ───────────────► Adapters ─────────────► Core
  │                     │                     │
  │ uses                │ uses                │ uses (interfaces)
  ▼                     ▼                     ▼
Platform.Abstractions   Client (SDK impl)     (no SDK refs)
```

依赖约束：

- Core 只能依赖抽象（接口/POCO）与自己的模块；禁止引用 Raylib/Web/Unity 具体 SDK。  
- Adapter/Client 可以引用平台 SDK，但不得把 SDK 类型泄漏到 Core 的接口签名。  

## 2.3 组件关系图（以 Raylib Desktop 为例）

```
Ludots.App.Raylib
  └─ RaylibGameHost (IGameHost)
       ├─ GameBootstrapper → GameEngine.Initialize(...)
       ├─ 注入平台服务到 GlobalContext (ContextKeys.*)
       ├─ while loop: dt = GetFrameTime()
       │      ├─ engine.Tick(dt)
       │      └─ Render (Raylib renderers + UI texture)
       └─ engine.Stop()
```

## 2.4 关联依赖（代码入口）

- App 入口：`src/Apps/Raylib/Ludots.App.Raylib/Program.cs`
- Host 与装配：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs`
- Bootstrap：`src/Core/Hosting/GameBootstrapper.cs`
- Engine：`src/Core/Engine/GameEngine.cs`
- 平台抽象：`src/Platform/Ludots.Platform.Abstractions/`
- 输入抽象：`src/Core/Input/Runtime/IInputBackend.cs`
- 相机抽象：`src/Core/Presentation/Camera/ICameraAdapter.cs`
- 坐标系策略：`src/Core/Presentation/Coordinates/CoordinateSystemFactory.cs`

# 3 业务设计

## 3.1 业务用例与边界

用例：

- 桌面运行：Raylib Host 提供窗口与渲染循环，Core 负责逻辑推进与表现缓冲。  
- Web 运行：Web 平台通过 DI 注册服务并驱动页面生命周期（当前作为对照链路存在）。  

边界：

- Core 不直接绘制，也不直接轮询平台输入；平台层负责采样并把结果投给 Core 的输入系统。  
- Core 允许通过 ContextKeys 获取平台能力，但必须假设能力可能不存在并做 fail-fast 或显式降级（由子系统裁决）。  

## 3.2 主流程（Raylib Desktop）

### 3.2.1 启动装配

1. App 创建 Host 并运行：`new RaylibGameHost(baseDir).Run()`  
2. Host 调用 bootstrap：`GameBootstrapper.InitializeFromBaseDirectory(baseDir)`  
   - 严格定位 `assets/`，严格读取 `game.json`，严格校验 `mod.json` 与 mod 路径。  
3. Host 注入平台能力到 `engine.GlobalContext`（示例）：
   - UI：`ContextKeys.UIRoot`、`ContextKeys.UISystem`
   - 输入：`ContextKeys.InputBackend`、`ContextKeys.InputHandler`
   - 投影/射线：`ContextKeys.ScreenProjector`、`ContextKeys.ScreenRayProvider`
4. Host 调用 `engine.Start()` 与 `engine.LoadMap(...)` 进入运行态。  

### 3.2.2 每帧循环

1. 平台获取 dt：`dt = GetFrameTime()`  
2. 平台采样输入并更新 UI：`UpdateInput(...)`  
3. Core 推进：`engine.Tick(dt)`  
4. 平台渲染：Raylib 渲染器消费 Core 产出的表现缓冲（DebugDraw/HUD/PrimitiveDraw 等）。  

## 3.3 关键场景与异常分支

- 启动失败：缺失 `game.json` / 缺失 `assets/` / mod 缺失清单文件必须直接失败并中止启动（fail-fast）。  
  - 入口：`src/Core/Hosting/GameBootstrapper.cs`
- 平台能力缺失：例如未注入 ScreenRayProvider，依赖方必须显式报错或关闭功能，不得静默 fallback。  
  - 入口：`src/Core/Scripting/ContextKeys.cs`

# 4 数据模型

## 4.1 概念模型

- `GameConfig (game.json)`：mod 清单与启动配置  
- `GlobalContext`：平台能力注入容器（键为 `ContextKeys` 常量）  

## 4.2 不变量

1. 平台 SDK 类型不出现在 Core 的公共接口签名中。  
2. `ContextKeys` 是注入键的单一真源，禁止魔法字符串散落。  
3. Host 的主循环是推进与渲染的唯一外层驱动点。  

# 5 落地方式

## 5.1 模块划分与职责

- Platform.Abstractions：定义最小宿主契约与通用能力接口。  
- Core：定义引擎生命周期、系统分组、逻辑推进与表现缓冲。  
- Client：实现平台输入/渲染等 SDK 绑定代码。  
- Adapter：创建 Host、装配 Core，并把平台能力注入 GlobalContext。  
- App：选择具体 Host 并运行。  

## 5.2 注入点（SSOT）

平台能力注入统一通过：

- `src/Core/Scripting/ContextKeys.cs`
- `GameEngine.GlobalContext[...]`

示例（Raylib）注入位置：

- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs`

## 5.3 运行时关键路径

- App → Host：`src/Apps/Raylib/Ludots.App.Raylib/Program.cs`
- Host → Bootstrap：`src/Core/Hosting/GameBootstrapper.cs`
- Bootstrap → Engine：`src/Core/Engine/GameEngine.cs`
- 每帧推进：`engine.Tick(dt)`（由 Host 驱动）

# 6 与其他模块的职责切分

## 6.1 切分结论

- “平台选择/窗口生命周期/渲染循环”属于 Host（Adapter）。  
- “逻辑推进/预算/Phase 顺序/TimeSlice”属于 Core（Engine）。  
- “平台能力实现（输入/渲染/射线）”属于 Client/Adapter（SDK 绑定处）。  

## 6.2 为什么如此

把 SDK 绑定隔离到平台层可以保证 Core 可测试、可复用，并允许多平台并行演进而不污染核心代码。

## 6.3 影响范围

- 新增平台必须新增 App/Adapter/Client 实现，并在 Host 装配期补齐必要的 ContextKeys 注入。  

# 7 当前代码现状

## 7.1 现状入口

- 平台抽象：`src/Platform/Ludots.Platform.Abstractions/`
- Raylib Host：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs`
- 引擎 bootstrap：`src/Core/Hosting/GameBootstrapper.cs`
- Web DI 入口：`src/Platforms/Web/Program.cs`

## 7.2 差距清单

| 设计口径 | 代码现状 | 差异等级 | 风险 | 证据 |
|---|---|---|---|---|
| 平台能力注入显式可追踪 | Raylib 侧通过 GlobalContext 注入 | 低 | 低 | `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibGameHost.cs` |
| 平台抽象统一承载渲染循环 | Raylib Host 自己驱动渲染与 Tick | 低 | 低 | `src/Platform/Ludots.Platform.Abstractions/IGameHost.cs` |
| Web 与 Core 主链一致 | Web 目前以 DI + 页面生命周期为主 | 中 | 中 | `src/Platforms/Web/Program.cs` |

## 7.3 迁移策略与风险

- 若要统一多平台运行链路，优先让 Web 侧也落到 “Host 驱动 Tick + 显式注入 ContextKeys” 的口径上，并给出对齐报告。  

# 8 验收条款

1. Core 不直接引用 Raylib/Web/Unity SDK。  
2. Host 能完成引擎 bootstrap、平台能力注入、主循环驱动与退出清理。  
3. 任意平台能力缺失都有可观测的 fail-fast 或显式降级路径。  
