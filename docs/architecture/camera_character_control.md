# 3C 系统：相机、角色与控制

## 1. 概述

3C 是游戏开发中三大核心交互管线的缩写：

| 缩写 | 全称 | Ludots 对应子系统 |
|------|------|-------------------|
| **C**amera | 相机 | CameraManager / CameraPresenter / CameraCullingSystem |
| **C**haracter | 角色 | WorldPositionCm → VisualTransform 管线 |
| **C**ontrol | 控制 | PlayerInputHandler / InputOrderMappingSystem |

### 总体数据流

```
┌──────────────────────────────────────────────────────────────────┐
│                        固定步 (Logic Tick)                        │
│                                                                  │
│  IInputBackend ──? PlayerInputHandler ──? InputOrderMappingSystem │
│                         │                       │                │
│                         │                  Order ──? GAS          │
│                         │                              │         │
│                         ▼                     ForceInput2D Sink  │
│          Core Camera Behavior Pipeline             │             │
│                    │                          Physics2D          │
│                    ▼                               │             │
│              CameraState                   Position2D            │
│                                                │                 │
│                                  Physics2DToWorldPositionSync    │
│                                                │                 │
│                                         WorldPositionCm          │
└──────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────┐
│                       渲染帧 (Render Frame)                       │
│                                                                  │
│  CameraManager.Update ──? CameraState (updated)                  │
│                                │                                 │
│  SavePreviousWorldPosition     │                                 │
│           │                    │                                 │
│  WorldToVisualSync (lerp α)    │                                 │
│           │                    │                                 │
│     VisualTransform        CameraPresenter ──? ICameraAdapter    │
│           │                    │                                 │
│     CameraCulling ?────────────┘                                 │
│           │                                                      │
│     CullState ──? Performer 发射                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 帧时序

- **Camera / Visual**：在渲染帧执行，使用 `float` 和插值 alpha
- **Logic（位置/物理/GAS）**：在固定步执行，使用 `Fix64` 确定性数学

---

## 2. 相机系统

### 2.1 CameraState — 相机状态数据

`src/Core/Gameplay/Camera/CameraState.cs`

纯数据容器，平台无关、可序列化。

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `TargetCm` | `Vector2` | (0, 0) | 相机注视目标（逻辑空间，厘米） |
| `Yaw` | `float` | 45° | 水平旋转角（度） |
| `Pitch` | `float` | 45° | 垂直俯仰角（度） |
| `DistanceCm` | `float` | 2000 | 相机到目标距离（厘米） |
| `RigKind` | `CameraRigKind` | `Orbit` | Core 定义的有限 rig 类型 |
| `ZoomLevel` | `int` | 5 | 离散缩放级别索引 |
| `FovYDeg` | `float` | 60° | 垂直视场角（度） |
| `IsFollowing` | `bool` | `false` | 当前帧是否处于跟随态 |

**单位约定**：逻辑空间统一使用**厘米 (cm)**，视觉空间使用**米 (m)**。转换通过 `WorldUnits.CmToM()` 完成。

### 2.2 Camera 请求主线

主线入口统一为 Core request：

- `CameraPresetRequest`：切换完整行为预设
- `CameraPoseRequest`：设置机位/朝向/距离/FOV
- `VirtualCameraRequest`：进入或清除临时镜头

推荐数据流：

```
Map / Mod / Trigger
    -> CameraPresetRequest / CameraPoseRequest / VirtualCameraRequest
    -> GameEngine.ApplyCamera*Request()
    -> CameraManager
    -> CameraPresenter / CameraViewportUtil
    -> ICameraAdapter
```

说明：

- Mod 只发请求，不直接装配 controller。
- 输入驱动的相机行为管线是 Core 内部实现细节，不再作为 Mod 扩展点暴露。

### 2.3 CameraPreset — 完整行为预设

`src/Core/Gameplay/Camera/CameraPreset.cs`

`CameraPreset` 现在不仅描述状态值，也描述运行时行为合同：

- `RigKind`：`Orbit` / `TopDown` / `ThirdPerson` / `FirstPerson`
- 输入行为：平移、旋转、缩放、拖拽
- 跟随行为：`FollowMode` + `FollowTargetKind`
- 约束：距离、俯仰、动作映射

也就是说，`MapConfig.DefaultCamera.PresetId` 的语义是“应用完整行为预设”，而不只是抹几个状态字段。

### 2.4 CameraManager — Core 相机运行时

`src/Core/Gameplay/Camera/CameraManager.cs`

```csharp
public class CameraManager
{
    public CameraState State { get; }
    public CameraPreset? ActivePreset { get; }
    public CameraFollowMode FollowMode { get; set; }
    public ICameraFollowTarget? FollowTarget { get; }
    public VirtualCameraBrain? VirtualCameraBrain { get; }

    public void ConfigureRuntime(PlayerInputHandler input, IViewController view);
    public void ApplyPreset(CameraPreset preset, ICameraFollowTarget? followTarget = null, bool snapToFollowTargetWhenAvailable = true);
    public void ApplyPose(CameraPoseRequest request);
    public void SetFollowTarget(ICameraFollowTarget? followTarget, bool snapToFollowTargetWhenAvailable = true);
    public void ActivateVirtualCamera(string id, float? blendDurationSeconds = null);
    public void ClearVirtualCamera();
    public void Update(float dt);
}
```

- `Update()` 负责统一执行：follow 解析 → 基础 controller 更新 → virtual camera 覆盖/混合。
- `ClearVirtualCamera()` 会回到当前基础相机状态，而不是停留在临时镜头。

### 2.5 Follow 与 VirtualCamera

跟随目标与临时镜头都收敛在 Core：

- 跟随目标接口：`src/Core/Gameplay/Camera/ICameraFollowTarget.cs`
- 典型实现：
  - `src/Core/Gameplay/Camera/FollowTargets/GlobalEntityFollowTarget.cs`
  - `src/Core/Gameplay/Camera/FollowTargets/FallbackChainFollowTarget.cs`
- VirtualCamera：
  - `src/Core/Gameplay/Camera/VirtualCameraDefinition.cs`
  - `src/Core/Gameplay/Camera/VirtualCameraRegistry.cs`
  - `src/Core/Gameplay/Camera/VirtualCameraBrain.cs`
  - `src/Core/Gameplay/Camera/VirtualCameraRequest.cs`

当前主线规则：

- 跟随目标由 Core 解析 `LocalPlayer` / `SelectedEntity` / `SelectedOrLocalPlayer`
- `VirtualCamera` 是基础 rig 之上的临时 override / blend 层
- `FirstPerson` / 零距离视角在 `CameraPresenter` / `CameraViewportUtil` 中有专门防护，避免 `NaN`

### 2.6 CameraPresenter — 表现层投影

`src/Core/Presentation/Camera/CameraPresenter.cs`

将逻辑 `CameraState` 转换为 3D 视觉坐标，发送给 `ICameraAdapter`。

**球坐标 → 笛卡尔公式**：

```
distM    = CmToM(DistanceCm)
hDist    = distM × cos(Pitch)
vDist    = distM × sin(Pitch)
offsetX  = hDist × sin(Yaw)
offsetZ  = -hDist × cos(Yaw)
position = target + (offsetX, vDist, offsetZ)
```

**Lerp 平滑**：
- 首帧直接 snap
- 后续帧：`t = clamp(SmoothSpeed × dt, 0, 1)`，`position = lerp(current, desired, t)`
- 默认 `SmoothSpeed = 10.0`

**万向锁防御**：
- 当 `|dot(forward, UnitY)| > 0.99` 时（即 Pitch ≈ 90°），Up 向量从 `UnitY` 切换为 `UnitZ`

**坐标转换**：
- 逻辑 XY 平面 → 视觉 XZ 平面
- cm → m：`WorldUnits.CmToM()`

### 2.7 CameraCullingSystem — 视锥裁剪与 LOD

`src/Core/Systems/CameraCullingSystem.cs`

**视口 AABB 公式**：

```
H = 2 × DistanceCm × tan(FovY/2)
H *= 1/max(sin(Pitch), 0.1)    // 俯仰补偿
W = H × AspectRatio
H *= 1.5                        // 安全边距
W *= 1.5
```

**LOD 阈值表**（距离为实体到相机目标的 2D 距离，单位 cm）：

| 距离范围 | LODLevel | IsVisible |
|----------|----------|-----------|
| < 4000 | `High` | true |
| 4000 – 10000 | `Medium` | true |
| 10000 – 20000 | `Low` | true |
| > 20000 | `Culled` | false |

**必需组件**：实体必须同时拥有 `WorldPositionCm` + `CullState` + `VisualModel` 三个组件才会被处理。

**CullState 组件**：

```csharp
public struct CullState
{
    public bool IsVisible;
    public LODLevel LOD;           // High=0, Medium=1, Low=2, Culled=255
    public float DistanceToCameraSq;
}
```

**帧间追踪**：使用双 `HashSet<Entity>` 交换，确保上一帧可见但本帧不再出现的实体被标记为 `Culled`。

---

## 3. 角色系统

### 3.1 位置真相链

```
Physics2D (Position2D)
    │ Physics2DToWorldPositionSyncSystem
    ▼
WorldPositionCm (Fix64Vec2, cm)  ← 单一真相源
    │ SavePreviousWorldPositionSystem
    ▼
PreviousWorldPositionCm (Fix64Vec2)
    │ WorldToVisualSyncSystem (lerp α)
    ▼
VisualTransform (Vector3, m)
```

- `WorldPositionCm`：Fix64Vec2，厘米单位，确定性位置的**唯一真相源 (SSOT)**
- `PreviousWorldPositionCm`：上一固定步的位置，用于渲染插值

### 3.2 物理层同步

`Physics2DToWorldPositionSyncSystem`：

```csharp
// Position2D.Value (Fix64Vec2) → WorldPositionCm.Value (Fix64Vec2)
// 直接赋值，Fix64 无舍入，位精确
```

- 在 `SystemGroup.PostMovement` 中执行
- 查询所有同时拥有 `Position2D` + `WorldPositionCm` 的实体

### 3.3 渲染插值

`WorldToVisualSyncSystem`：

```csharp
// lerp = Fix64Vec2.Lerp(Previous, Current, alpha)
// VisualTransform.Position = (lerp.X * 0.01, 0, lerp.Y * 0.01)  // XY逻辑 → XZ视觉，cm→m
```

- `InterpolationAlpha` 从 `PresentationFrameState` 单例组件读取
- XY 逻辑平面映射到 XZ 视觉空间（Y 轴为高度，默认 0）

### 3.4 CullState 优化

- `IsVisible = false` 的实体跳过 `WorldToVisualSyncSystem` 处理
- 无 `CullState` 组件的实体始终同步（向后兼容）
- `CullState` 贯穿到 Performer 发射层：不可见实体不产生渲染指令

---

## 4. 控制系统

### 4.1 IInputBackend — 平台抽象

`src/Core/Input/Runtime/IInputBackend.cs`

```csharp
public interface IInputBackend
{
    bool GetButton(string devicePath);      // e.g. "<Keyboard>/w"
    float GetAxis(string devicePath);       // e.g. "<Mouse>/Scroll"
    Vector2 GetMousePosition();
    float GetMouseWheel();
    void EnableIME(bool enable);
    void SetIMECandidatePosition(int x, int y);
    string GetCharBuffer();
}
```

**设备路径格式**：`<Device>/Key`，如 `<Keyboard>/w`、`<Mouse>/LeftButton`、`<Mouse>/Scroll`

### 4.2 PlayerInputHandler — Action 状态机

`src/Core/Input/Runtime/PlayerInputHandler.cs`

**Context 栈**：
- `PushContext(contextId)` / `PopContext(contextId)` — 按优先级排序
- 高优先级 Context 的绑定优先匹配

**边缘检测**：
- `PressedThisFrame(actionId)` — 本帧首次按下
- `ReleasedThisFrame(actionId)` — 本帧首次松开
- `IsDown(actionId)` — 当前帧是否按住

**处理器管线**：

```
原始输入 → Deadzone → Normalize → Scale → 最终值
```

每个 Binding 可配置处理器链。

**Composite Input**:
- `Vector2Composite`: WASD -> 2D direction vector
- Configure via `InputBindingDef.CompositeType`
- Runtime precompiles context / binding / processor chains and accumulates by action index to avoid per-frame string lookups on the fixed-step hot path

**InputBlocked**：
- `handler.InputBlocked = true` → 所有 action 读取返回默认值

### 4.3 InputOrderMappingSystem — 指令映射

`src/Core/Input/Orders/InputOrderMappingSystem.cs`

将 InputAction 触发转换为 GAS Order。

**4 种交互模式**：

| 模式 | 说明 | 典型游戏 |
|------|------|----------|
| `TargetFirst` | 按键 → 立即提交（使用已选目标） | WoW |
| `SmartCast` | 按键 → 立即提交（使用光标/悬停目标） | LoL |
| `AimCast` | 按键 → 进入瞄准 → 确认点击 → 提交 | DotA |
| `SmartCastWithIndicator` | 按下 → 显示指示器 → 松开 → 提交 | LoL (按住施法) |

非技能映射（`IsSkillMapping = false`）始终使用 TargetFirst 行为。

**6 种选择类型**：

| 类型 | 说明 |
|------|------|
| `None` | 自我施法或无目标 |
| `Position` | 地面位置 |
| `Entity` | 单个实体 |
| `Direction` | 方向（actor → 光标） |
| `Vector` | 两点向量（起点 + 终点），两阶段瞄准 |
| `Entities` | 多个实体（预留） |

**Provider 回调**：

```csharp
system.SetGroundPositionProvider((out Vector3 pos) => { ... });
system.SetSelectedEntityProvider((out Entity e) => { ... });
system.SetHoveredEntityProvider((out Entity e) => { ... });
system.SetOrderSubmitHandler((in Order order) => { ... });
system.SetOrderTypeKeyResolver(key => orderTypeRegistry.Get(key));
system.SetAutoTargetProvider((actor, policy, range, out target) => { ... });
```

---

## 5. Mod 扩展指南

### 5.1 请求式相机扩展

Mod 只负责发起请求，不直接注册、切换或装配 controller。

1. 在 `assets/Configs/Camera/presets.json` 中声明或覆盖 `CameraPreset`
2. 在 Trigger / Map 初始化里发 `CameraPresetRequest`
3. 需要覆盖机位时，再叠加 `CameraPoseRequest`
4. 需要临时演出镜头时，发 `VirtualCameraRequest`

```csharp
engine.SetService(CoreServiceKeys.CameraPresetRequest, new CameraPresetRequest
{
    PresetId = "Rts"
});

engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
{
    TargetCm = new Vector2(50000f, 50000f),
    Pitch = 60f,
    DistanceCm = 12000f
});

engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
{
    Id = "FocusEnemy",
    BlendDurationSeconds = 0.15f
});
```

约束：

- Adapter 层只消费 `CameraRenderState3D`，不承载相机决策。
- Core 持有 follow、virtual camera、rig 数学和运行时状态。
- Mod 仅配置预设、设置机位、请求临时镜头。

### 5.2 自定义输入映射

1. 编写 `default_input.json`（定义 Actions + Contexts + Bindings）
2. 编写 `input_order_mappings.json`（定义 ActionId → OrderTypeKey 映射）
3. 在 Trigger 中加载并设置 Provider 回调

```json
// default_input.json（部分）
{
  "actions": [
    { "id": "Skill_Q", "type": "Button" },
    { "id": "Skill_W", "type": "Button" }
  ],
  "contexts": [
    {
      "id": "Gameplay",
      "priority": 10,
      "bindings": [
        { "actionId": "Skill_Q", "path": "<Keyboard>/q" },
        { "actionId": "Skill_W", "path": "<Keyboard>/w" }
      ]
    }
  ]
}
```

```json
// input_order_mappings.json
{
  "interactionMode": "SmartCast",
  "mappings": [
    {
      "actionId": "Skill_Q",
      "trigger": "PressedThisFrame",
      "orderTagKey": "ability.q",
      "isSkillMapping": true,
      "selectionType": "Position"
    }
  ]
}
```

---

## 6. 集成管线速查

### Camera → CameraCulling → Performer 贯通

```
CameraManager.Update(dt)
  └── Core behavior pipeline.Update(CameraState, dt)
        └── 修改 CameraState

CameraPresenter.Update(CameraState, dt)
  ├── 球坐标→笛卡尔 → Lerp 平滑
  └── ICameraAdapter.UpdateCamera(CameraRenderState3D)

CameraCullingSystem.Update(dt)
  ├── 计算视口 AABB (FOV + Pitch + AspectRatio)
  ├── ISpatialQueryService.QueryAabb()
  ├── 视口内检查 + 距离→LOD
  └── 更新 CullState { IsVisible, LOD, DistanceToCameraSq }

Performer 发射
  └── 检查 CullState.IsVisible → 跳过不可见实体
```

### Input → Order → GAS → Physics → WorldPosition 贯通

```
IInputBackend (平台原始输入)
  └── PlayerInputHandler.Update()
        ├── Context 栈按优先级匹配 Binding
        ├── 边缘检测 (PressedThisFrame / ReleasedThisFrame)
        └── 处理器管线 (Deadzone → Normalize → Scale)

InputOrderMappingSystem.Update(dt)
  ├── 遍历 mappings, 检查 trigger
  ├── 交互模式分发 (TargetFirst / SmartCast / AimCast)
  └── OrderSubmitHandler(Order)

GAS Effect Pipeline
  └── ForceInput2D Sink → Physics2D

Physics2D Step
  └── Position2D 更新

Physics2DToWorldPositionSyncSystem
  └── Position2D → WorldPositionCm (Fix64 直赋)

SavePreviousWorldPositionSystem
  └── WorldPositionCm → PreviousWorldPositionCm

WorldToVisualSyncSystem
  └── Lerp(Previous, Current, α) → VisualTransform (cm→m, XY→XZ)
```

### 完整帧序列

```
═══════════ 固定步 (16.67ms / 60Hz) ═══════════
  InputCollection          → PlayerInputHandler.Update()
  PostMovement             → Physics2DToWorldPositionSync
  AbilityActivation        → InputOrderMappingSystem.Update()
  EffectProcessing         → GAS pipeline
  AttributeCalculation     → Attribute aggregation

═══════════ 渲染帧 (可变帧率) ════════════════════
  SavePreviousWorldPosition
  CameraManager.Update(renderDt)
  CameraPresenter.Update(state, renderDt)
  WorldToVisualSyncSystem(α)
  CameraCullingSystem
  Performer 发射
  ICameraAdapter.UpdateCamera() → 平台渲染
```

---

## 7. 相关文件索引

### Core Camera

| 文件 | 说明 |
|------|------|
| `src/Core/Gameplay/Camera/CameraState.cs` | 相机状态数据 |
| `src/Core/Gameplay/Camera/CameraManager.cs` | 中央管理服务 |
| `src/Core/Gameplay/Camera/CameraPresetRegistry.cs` | 相机预设注册表 |
| `src/Core/Gameplay/Camera/CameraPresetRequest.cs` | 相机预设请求 |
| `src/Core/Gameplay/Camera/CameraPoseRequest.cs` | 相机机位请求 |
| `src/Core/Gameplay/Camera/VirtualCameraRegistry.cs` | 临时镜头注册表 |
| `src/Core/Gameplay/Camera/VirtualCameraRequest.cs` | 临时镜头请求 |
| `src/Core/Gameplay/Camera/CameraControllerFactory.cs` | Core 内部行为装配 |
| `src/Core/Gameplay/Camera/Behaviors/*.cs` | Core 内部输入行为单元 |

### Core Presentation

| 文件 | 说明 |
|------|------|
| `src/Core/Presentation/Camera/CameraPresenter.cs` | 球坐标投影 + Lerp |
| `src/Core/Presentation/Camera/ICameraAdapter.cs` | 平台渲染接口 |
| `src/Core/Presentation/Camera/IViewController.cs` | 视口属性接口 |
| `src/Core/Presentation/Camera/CameraRenderState3D.cs` | 渲染状态结构 |
| `src/Core/Presentation/Components/CullState.cs` | 裁剪/LOD 组件 |
| `src/Core/Presentation/Components/VisualTransform.cs` | 视觉变换组件 |
| `src/Core/Presentation/Components/VisualModel.cs` | 视觉模型组件 |
| `src/Core/Systems/CameraCullingSystem.cs` | 视锥裁剪系统 |

### Core Input

| 文件 | 说明 |
|------|------|
| `src/Core/Input/Runtime/IInputBackend.cs` | 平台输入抽象 |
| `src/Core/Input/Runtime/PlayerInputHandler.cs` | Action 状态机 |
| `src/Core/Input/Config/InputConfigModels.cs` | 输入配置模型 |
| `src/Core/Input/Orders/InputOrderMappingSystem.cs` | 指令映射系统 |

### Core 投影与 HUD

| 文件 | 说明 |
|------|------|
| `src/Core/Gameplay/Camera/CameraViewportUtil.cs` | 视口公式、WorldToScreen 纯数学实现 |
| `src/Core/Presentation/Camera/CoreScreenProjector.cs` | 实现 IScreenProjector，平台无关 |
| `src/Core/Presentation/Hud/ScreenHudBatchBuffer.cs` | 屏幕空间 HUD 缓冲 |
| `src/Core/Presentation/Systems/WorldHudToScreenSystem.cs` | WorldHud → ScreenHud 投影与裁切 |
| `src/Core/Gameplay/Camera/CameraPreset.cs` | 相机预设数据结构 |
| `src/Core/Gameplay/Camera/CameraPresetRegistry.cs` | 预设注册表 |
| `src/Core/Gameplay/Camera/CameraPresetLoader.cs` | 从 ConfigPipeline 加载预设 |

### Adapter

| 文件 | 说明 |
|------|------|
| `src/Adapters/Raylib/.../RaylibCameraAdapter.cs` | Raylib 相机实现 |
| `src/Adapters/Raylib/.../RaylibViewController.cs` | IViewController 实现（Resolution/AspectRatio） |
| `src/Client/Ludots.Client.Raylib/.../RaylibInputBackend.cs` | Raylib 输入实现 |
| `src/Client/Ludots.Client.Raylib/.../RaylibScreenRayProvider.cs` | Screen→World 射线 |

### 交叉引用

- [适配器原则与平台抽象](adapter_pattern.md) — Core/Adapter 边界
- [Pacemaker 时间与步进](pacemaker.md) — 固定步/渲染帧时序
- [表现管线与 Performer 体系](presentation_performer.md) — Performer 发射与 CullState
- [Trigger 开发指南](trigger_guide.md) — Mod 中注册相机/输入
- [Map、Mod 与空间服务可插拔](map_mod_spatial.md) — ISpatialQueryService
- [GAS 分层架构与 Sink](gas_layered_architecture.md) — ForceInput2D Sink


