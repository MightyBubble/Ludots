# 3C 系统：相机、角色与控制

## 1. 概述

3C 的当前实现目标是三件事同时成立：

1. 输入、逻辑相机、Order 在同一个 fixed-step 快照上推进
2. 表现层只做插值和投影，不再偷跑第二套逻辑
3. 相机一切皆为 virtual camera，profile 与 shot 共用一套运行时

核心证据路径：

- 相机逻辑：`src/Core/Gameplay/Camera/CameraManager.cs`
- virtual camera 栈：`src/Core/Gameplay/Camera/VirtualCameraBrain.cs`
- fixed-step 请求消费：`src/Core/Systems/CameraRuntimeSystem.cs`
- 输入冻结：`src/Core/Input/Systems/AuthoritativeInputSnapshotSystem.cs`
- 表现投影：`src/Core/Presentation/Camera/CameraPresenter.cs`

## 2. 总体数据流

### 固定步

```text
InputRuntimeSystem
  -> AuthoritativeInputAccumulator
  -> AuthoritativeInputSnapshotSystem
  -> CoreServiceKeys.AuthoritativeInput
  -> CameraRuntimeSystem
  -> InputOrderMappingSystem / Select / GAS Response
  -> GAS / Physics2D / Navigation2D
  -> WorldPositionCm
```

### 渲染帧

```text
PlayerInputHandler.Update()
  -> InputRuntimeSystem
  -> CameraManager.CaptureVisualInput()
  -> PresentationFrameSetupSystem(alpha)
  -> WorldToVisualSyncSystem
  -> CameraPresenter(cameraManager, alpha)
  -> CameraCullingSystem
  -> Performer / Adapter
```

关键约束：

- `assets/Configs/Engine/clock.json` 当前固定步为 30 Hz
- `CameraRuntimeSystem` 与 `InputOrderMappingSystem` 同属 `SystemGroup.InputCollection`
- `CameraPresenter` 只能消费 `CameraManager.GetInterpolatedState(alpha)`

## 3. 相机架构

### 3.1 CameraManager

`CameraManager` 只负责 authoritative logic camera：

- 维护 `State` / `PreviousState`
- 冻结并消费相机输入快照
- 调用 `VirtualCameraBrain` 解析当前权威 virtual camera
- 只在当前权威 virtual camera 允许输入时挂载 controller

它不再承担以下职责：

- 不再保存 `ActivePreset`
- 不再做 preset / shot 两套分叉状态
- 不再维护“切 shot 前的 pre-state 恢复栈”

### 3.2 VirtualCameraBrain

`VirtualCameraBrain` 的职责是“virtual camera stack SSOT”：

- active camera 集合
- per-camera runtime state
- priority / activation sequence 排序
- follow target 解析结果
- blend 起点和 blend 进度

当前规则：

1. profile 和 shot 都是 `VirtualCameraDefinition`
2. authority 取 `priority desc + activationSequence desc`
3. `Clear=true` 只移除当前栈顶
4. 回落时继续沿用下一台 virtual camera 的 runtime state
5. blend 起点来自当前逻辑输出快照，而不是裸 `CameraState`

这保证了两类情况正确：

- 对底层基础机位先做 `CameraPoseRequest`，再激活高优先级 shot
- 清理 shot 后，底层跟随机位能够按已解析到的跟随目标继续接管

### 3.3 VirtualCameraDefinition

`src/Core/Gameplay/Camera/VirtualCameraDefinition.cs` 现在承载完整相机行为合同：

- 机位：`Yaw` / `Pitch` / `DistanceCm` / `FovYDeg`
- rig：`RigKind`
- 输入映射：`MoveActionId` / `ZoomActionId` / `Rotate*ActionId`
- 跟随：`FollowMode` + 上层显式 `ICameraFollowTarget` 绑定
- 过渡：`DefaultBlendDuration` / `BlendCurve`
- 权限：`AllowUserInput`

含义：

- profile 只是“可长期常驻的 virtual camera”
- shot 只是“更高优先级的 virtual camera”
- 不再存在 runtime 上的 preset 特权

### 3.4 Request 主线

运行时只接受两种 camera request：

- `src/Core/Gameplay/Camera/VirtualCameraRequest.cs`
- `src/Core/Gameplay/Camera/CameraPoseRequest.cs`

约定：

- `VirtualCameraRequest.Id` 用于激活指定 virtual camera
- `VirtualCameraRequest.Clear=true` 用于清栈顶或清指定 id
- `CameraPoseRequest.VirtualCameraId` 用于精确修改某台 active virtual camera 的 runtime pose

### 3.5 Presenter 与 Tween

当前有且只有两层平滑：

1. 逻辑层：`VirtualCameraBrain` 的 `TweenProgress`
2. 表现层：`CameraPresenter` 对 `PreviousState -> State` 的插值

因此：

- 真正的镜头过渡必须走 virtual camera blend
- Presenter 不能再自己插一套“临时平滑镜头”
- Adapter 只能消费 render state，不能决定逻辑镜头权威

## 4. 输入与相机的绑定关系

输入与逻辑相机是强绑定关系，必须共用 fixed-step snapshot。

实际落点：

- live 输入采样：`src/Core/Input/Systems/InputRuntimeSystem.cs`
- 权威输入冻结：`src/Core/Input/Systems/AuthoritativeInputSnapshotSystem.cs`
- 相机固定步推进：`src/Core/Systems/CameraRuntimeSystem.cs`
- 输入到 Order：`mods/CoreInputMod/Systems/ViewModeSwitchSystem.cs`、`src/Core/Input/Orders/InputOrderMappingSystem.cs`

时序原则：

- 先冻结输入，再推进相机，再推进 Order / GAS
- `CameraManager.CaptureVisualInput()` 只采样，不推进逻辑
- `ViewModeManager` 改变的是 virtual camera 激活关系，不绕过 fixed-step 主线

## 5. 角色与相机的耦合点

角色位置真相链：

```text
Physics2D.Position2D
  -> Physics2DToWorldPositionSyncSystem
  -> WorldPositionCm
  -> SavePreviousWorldPositionSystem
  -> PreviousWorldPositionCm
  -> WorldToVisualSyncSystem
  -> VisualTransform
```

相机只读两类角色信息：

- 跟随目标位置：通过 `ICameraFollowTarget`
- 裁剪参考位置：通过 `WorldPositionCm`

这样可以保证：

- 逻辑跟随始终基于确定性坐标
- 表现插值不会反向污染逻辑相机

## 6. Mod 扩展面

推荐扩展点：

- 基础 profile：`mods/capabilities/camera/CameraProfilesMod/assets/Configs/Camera/virtual_cameras.json`
- 视角模式：`mods/capabilities/camera/CameraProfilesMod/assets/viewmodes.json`
- shot：`mods/capabilities/camera/VirtualCameraShotsMod/assets/Configs/Camera/virtual_cameras.json`
- 生产级示例：`mods/showcases/camera/CameraShowcaseMod`
- 地图默认相机：`MapConfig.DefaultCamera.VirtualCameraId`
- 地图演出标签：例如 `camera.shot:Camera.Shot.IntroFocus`

不推荐扩展点：

- 不再新增 `CameraPresetRegistry`
- 不再新增 `CameraControllerRequest`
- 不在 Mod 内保存一份独立 camera state

## 7. 当前验收证据

关键测试：

- `src/Tests/GasTests/CameraRuntimeConvergenceTests.cs`
- `src/Tests/GasTests/ThreeCSystemTests.cs`
- `src/Tests/GasTests/Production/CameraCapabilityModTests.cs`
- `src/Tests/GasTests/Production/CameraShowcaseModTests.cs`

关键验收产物：

- `artifacts/acceptance/camera-capability-mods/battle-report.md`
- `artifacts/acceptance/camera-capability-mods/trace.jsonl`
- `artifacts/acceptance/camera-capability-mods/path.mmd`
- `artifacts/acceptance/camera-showcase-mod/battle-report.md`
- `artifacts/acceptance/camera-showcase-mod/trace.jsonl`
- `artifacts/acceptance/camera-showcase-mod/path.mmd`
