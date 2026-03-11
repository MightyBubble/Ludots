---
文档类型: 开发指南
创建日期: 2026-03-02
维护人: X28技术团队
文档版本: v2.0
适用范围: 3C Camera 标准
---

# Camera 标准规范

## 概述

当前相机体系的标准模型是“全部皆为 virtual camera”。`src/Core/Gameplay/Camera/VirtualCameraDefinition.cs` 同时承担 profile 和 shot 两种用途：

- profile：长期存在的基础机位，例如 `Camera.Profile.Follow`
- shot：短时覆盖的高优先级镜头，例如 `Camera.Shot.IntroFocus`

运行时由 `src/Core/Gameplay/Camera/VirtualCameraBrain.cs` 按优先级和激活序列解析唯一权威机位，再由 `src/Core/Gameplay/Camera/CameraManager.cs` 推进逻辑状态。

## SSOT 边界

- 逻辑相机 SSOT：`src/Core/Gameplay/Camera/CameraManager.cs` 的 `State` / `PreviousState`
- 虚拟相机栈 SSOT：`src/Core/Gameplay/Camera/VirtualCameraBrain.cs` 的 active set、priority、runtime state
- 输入 SSOT：`src/Core/Input/Systems/AuthoritativeInputSnapshotSystem.cs` 写入的 `CoreServiceKeys.AuthoritativeInput`
- 表现相机 SSOT：`src/Core/Presentation/Camera/CameraPresenter.cs` 基于 `alpha` 插值得到的渲染态

约束：

- 逻辑相机只在固定步 `SystemGroup.InputCollection` 中推进
- 表现层不再维护第二套镜头 tween
- 相机行为与输入读取必须共用固定步快照，不得跨层各自 tick

## Virtual Camera 栈

每个 virtual camera 都由 `Camera/virtual_cameras.json` 通过 `src/Core/Gameplay/Camera/VirtualCameraDefinitionLoader.cs` 加载进 `src/Core/Gameplay/Camera/VirtualCameraRegistry.cs`。

关键字段：

- `priority`：决定谁拥有权威
- `rigKind`：`Orbit` / `TopDown` / `ThirdPerson` / `FirstPerson`
- `targetSource`：`CurrentState` / `Fixed` / `FollowTarget`
- `followMode` / `followTargetKind`：决定跟随策略
- `defaultBlendDuration` / `blendCurve`：决定逻辑层镜头过渡
- `allowUserInput`：决定当前权威相机是否允许输入驱动

解析规则：

1. 先按 `priority` 取最大值
2. 同优先级按最近激活序列取胜
3. `Clear=true` 只清当前栈顶 authoritative camera
4. 清栈顶后自动回落到下一台 active virtual camera
5. 回落同样遵循目标 virtual camera 的 blend 配置

## MapConfig.DefaultCamera

地图默认相机现在通过 `MapConfig.DefaultCamera.VirtualCameraId` 声明，语义是“激活哪台基础 virtual camera”，而不是“应用 preset”。

示例：

```json
{
  "Id": "camera_acceptance_entry",
  "DefaultCamera": {
    "VirtualCameraId": "Camera.Profile.Follow",
    "TargetXCm": 1200,
    "TargetYCm": 800
  }
}
```

加载顺序见 `src/Core/Engine/GameEngine.cs` 的 `ApplyDefaultCamera(MapConfig)`：

1. `ResetVirtualCameras()`
2. 激活 map default virtual camera
3. 通过 `CameraPoseRequest` 应用地图级 pose override
4. 触发 `MapLoaded`，允许 Mod 继续加 shot 或补 pose

## Request 主线

当前主线只允许以下两个请求进入 Core：

- `src/Core/Gameplay/Camera/VirtualCameraRequest.cs`
- `src/Core/Gameplay/Camera/CameraPoseRequest.cs`

推荐写法：

```csharp
engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
{
    Id = "Camera.Shot.IntroFocus"
});

engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
{
    VirtualCameraId = "Camera.Profile.Follow",
    DistanceCm = 25000f,
    Pitch = 60f
});
```

禁止行为：

- 直接写 `session.Camera.State`
- 在 Adapter / Presenter / Mod 内再造一套 camera tween
- 引入 `CameraPresetRequest`、`ApplyPreset()` 之类的旧入口

## 输入与过渡时序

相关代码路径：

- live 输入采样：`src/Core/Input/Systems/InputRuntimeSystem.cs`
- 权威输入冻结：`src/Core/Input/Systems/AuthoritativeInputSnapshotSystem.cs`
- 相机逻辑推进：`src/Core/Systems/CameraRuntimeSystem.cs`
- 相机逻辑状态：`src/Core/Gameplay/Camera/CameraManager.cs`
- 过渡曲线：`src/Core/Tweening/TweenEasing.cs`
- 表现插值：`src/Core/Presentation/Camera/CameraPresenter.cs`

规则：

- 输入在哪个 fixed-step snapshot 被消费，相机逻辑就在哪个 snapshot 推进
- `VirtualCameraBrain` 的 blend 是逻辑层过渡，不是纯视觉补帧
- `CameraPresenter` 只负责 `PreviousState -> State` 的渲染插值
- 逻辑层默认 fixed 30 Hz，见 `assets/Configs/Engine/clock.json`

## Mod 编写规范

当前推荐的 camera mod 分层：

- `mods/capabilities/camera/CameraProfilesMod`
  - 提供可复用基础 virtual camera profile
  - 提供 `viewmodes.json`
- `mods/capabilities/camera/VirtualCameraShotsMod`
  - 提供声明式 shot
  - 通过 map tag 或 trigger 激活
- `mods/capabilities/camera/CameraBootstrapMod`
  - 只负责地图空间到相机 pose 的启动补正
- `mods/fixtures/camera/CameraAcceptanceMod`
  - 提供最小验收地图和基础夹具
- `mods/showcases/camera/CameraShowcaseMod`
  - 提供生产级 camera 示例：共享 profile、局部 selection-follow profile、shot 栈、bootstrap、runtime pose override

编写要求：

- profile 和 shot 都必须落到 `virtual_cameras.json`
- 视角模式切换通过 `mods/CoreInputMod/ViewMode/ViewModeManager.cs`
- 通用选择 / ViewMode / TabTarget 输入绑定收口在 `mods/CoreInputMod/assets/Input/default_input.json`
- 地图级默认机位只写 `DefaultCamera.VirtualCameraId`
- 短时镜头通过 `VirtualCameraRequest`

## 已知迁移点

以下内容仍应继续收口到 virtual camera 主线：

| 模块 | 当前状态 | 目标 |
|:--|:--|:--|
| `TerrainBenchmarkMod` | 仍在 trigger 里补 pose | 尽量把基础机位前移到 `DefaultCamera` |
| `Physics2DPlaygroundMod` | 仍有场景特定 pose override | 保留 override，避免重复声明基础 virtual camera |
| `Navigation2DPlaygroundMod` | 仍有场景特定 pose override | 同上 |
| `PerformanceVisualizationMod` | 仍有远景 pose override | 同上 |

## 废弃项

- `CameraPreset.cs`
- `CameraPresetLoader.cs`
- `CameraPresetRegistry.cs`
- `CameraPresetRequest.cs`
- 任何“preset + overlay shot + restore pre-state”的并行心智模型
