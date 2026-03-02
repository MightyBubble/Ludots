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
│  IInputBackend ──► PlayerInputHandler ──► InputOrderMappingSystem │
│                         │                       │                │
│                         │                  Order ──► GAS          │
│                         │                              │         │
│                         ▼                     ForceInput2D Sink  │
│              ICameraController                     │             │
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
│  CameraManager.Update ──► CameraState (updated)                  │
│                                │                                 │
│  SavePreviousWorldPosition     │                                 │
│           │                    │                                 │
│  WorldToVisualSync (lerp α)    │                                 │
│           │                    │                                 │
│     VisualTransform        CameraPresenter ──► ICameraAdapter    │
│           │                    │                                 │
│     CameraCulling ◄────────────┘                                 │
│           │                                                      │
│     CullState ──► Performer 发射                                  │
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
| `ZoomLevel` | `int` | 5 | 离散缩放级别索引 |
| `FovYDeg` | `float` | 60° | 垂直视场角（度） |

**单位约定**：逻辑空间统一使用**厘米 (cm)**，视觉空间使用**米 (m)**。转换通过 `WorldUnits.CmToM()` 完成。

### 2.2 ICameraController — 控制器接口

`src/Core/Gameplay/Camera/ICameraController.cs`

```csharp
public interface ICameraController
{
    void Update(CameraState state, float dt);
}
```

- 接收可变的 `CameraState` 引用，直接修改状态
- `dt` 为渲染帧增量时间（秒）
- 内置实现：`Orbit3CCameraController`（3C 轨道相机）

### 2.3 Orbit3CCameraConfig — 配置项

`src/Core/Gameplay/Camera/CameraControllerRequest.cs`（与 `CameraControllerRequest` 同文件）

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `EnablePan` | `true` | 是否允许键盘平移 |
| `MoveActionId` | `"Move"` | 平移输入 Action |
| `ZoomActionId` | `"Zoom"` | 缩放输入 Action |
| `PointerPosActionId` | `"PointerPos"` | 鼠标位置 Action |
| `RotateHoldActionId` | `"OrbitRotateHold"` | 旋转按住 Action |
| `RotateLeftActionId` / `RotateRightActionId` | `"RotateLeft"` / `"RotateRight"` | 键盘旋转 Action |
| `RotateDegPerPixel` | 0.28 | 鼠标拖拽灵敏度 |
| `ZoomCmPerWheel` | 2000 | 每滚轮刻度距离变化 |
| `PanCmPerSecond` | 6000 | 平移速度 |
| `RotateDegPerSecond` | 90 | 键盘旋转速度 |
| `MinPitchDeg` / `MaxPitchDeg` | 10° / 85° | 俯仰角夹钳 |
| `MinDistanceCm` / `MaxDistanceCm` | 500 / 200000 | 距离夹钳 |

### 2.4 CameraManager — 中央服务

`src/Core/Gameplay/Camera/CameraManager.cs`

```csharp
public class CameraManager
{
    public CameraState State { get; }      // 当前相机状态
    public ICameraController Controller { get; }

    public void SetController(ICameraController controller);
    public void Update(float dt);           // 委托给 Controller.Update()
}
```

- 初始化时 `State` 为默认 `CameraState`
- `Controller` 初始为 null；`Update()` 对 null 安全

### 2.5 CameraControllerRegistry — 工厂注册

`src/Core/Gameplay/Camera/CameraControllerRegistry.cs`

```csharp
public sealed class CameraControllerRegistry
{
    // 注册控制器工厂
    public void Register(string id, Func<object?, CameraControllerBuildServices, ICameraController> factory);

    // 按 CameraControllerRequest 创建控制器
    public ICameraController Create(CameraControllerRequest request, CameraControllerBuildServices services);
}
```

**Mod 注册自定义控制器示例**：

```csharp
// 在 Mod 的 OnLoad Trigger 中
registry.Register("MyTopDown", (config, services) =>
{
    var cfg = config as MyTopDownConfig ?? new MyTopDownConfig();
    return new MyTopDownController(cfg, services.Input);
});
```

通过 `CameraControllerRequest { Id = "MyTopDown", Config = myConfig }` 切换。

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

**Composite 输入**：
- `Vector2Composite`：WASD → 2D 方向向量
- 通过 `InputBindingDef.CompositeType` 配置

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
system.SetTagKeyResolver(key => tagRegistry.GetId(key));
system.SetAutoTargetProvider((actor, policy, range, out target) => { ... });
```

---

## 5. Mod 扩展指南

### 5.1 自定义相机控制器

1. 实现 `ICameraController` 接口
2. 在 OnLoad Trigger 中注册到 `CameraControllerRegistry`
3. 通过 `CameraControllerRequest` 切换

```csharp
// MyRtsCameraController.cs
public class MyRtsCameraController : ICameraController
{
    private readonly PlayerInputHandler _input;

    public MyRtsCameraController(PlayerInputHandler input) => _input = input;

    public void Update(CameraState state, float dt)
    {
        // 自定义逻辑：边缘滚动、选框拖拽等
        var move = _input.ReadAction<Vector2>("Move");
        state.TargetCm += move * 8000f * dt;
    }
}

// 在 Mod OnLoad 中注册
cameraControllerRegistry.Register("RtsCamera", (config, services) =>
    new MyRtsCameraController(services.Input));

// 切换相机
cameraManager.SetController(
    cameraControllerRegistry.Create(
        new CameraControllerRequest { Id = "RtsCamera" },
        new CameraControllerBuildServices(inputHandler)));
```

### 5.2 自定义输入映射

1. 编写 `default_input.json`（定义 Actions + Contexts + Bindings）
2. 编写 `input_order_mappings.json`（定义 ActionId → OrderTagKey 映射）
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
  └── ICameraController.Update(CameraState, dt)
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
| `src/Core/Gameplay/Camera/ICameraController.cs` | 控制器接口 |
| `src/Core/Gameplay/Camera/CameraManager.cs` | 中央管理服务 |
| `src/Core/Gameplay/Camera/CameraControllerRegistry.cs` | 工厂注册表 |
| `src/Core/Gameplay/Camera/CameraControllerRequest.cs` | 请求 + Orbit3C 配置 |
| `src/Core/Gameplay/Camera/Orbit3CCameraController.cs` | 轨道相机控制器 |
| `src/Core/Gameplay/Camera/CameraLogic.cs` | **(死代码)** 已被 ICameraController 取代 |

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
| `src/Core/Input/Config/InputConfigRoot.cs` | 输入配置模型 |
| `src/Core/Input/Orders/InputOrderMappingSystem.cs` | 指令映射系统 |

### Adapter

| 文件 | 说明 |
|------|------|
| `src/Adapters/Raylib/.../RaylibCameraAdapter.cs` | Raylib 相机实现 |
| `src/Client/Ludots.Client.Raylib/.../RaylibInputBackend.cs` | Raylib 输入实现 |
| `src/Client/Ludots.Client.Raylib/.../RaylibScreenRayProvider.cs` | Screen→World 射线 |
| `src/Client/Ludots.Client.Raylib/.../RaylibScreenProjector.cs` | World→Screen 投影 |

### 交叉引用

- [适配器原则与平台抽象](03_adapter_pattern.md) — Core/Adapter 边界
- [Pacemaker 时间与步进](05_pacemaker.md) — 固定步/渲染帧时序
- [表现管线与 Performer 体系](06_presentation_performer.md) — Performer 发射与 CullState
- [Trigger 开发指南](08_trigger_guide.md) — Mod 中注册相机/输入
- [Map、Mod 与空间服务可插拔](10_map_mod_spatial.md) — ISpatialQueryService
- [GAS 分层架构与 Sink](11_gas_layered_architecture.md) — ForceInput2D Sink
