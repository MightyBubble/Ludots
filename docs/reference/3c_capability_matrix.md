# 3C 系统能力清单

## 相机 (Camera)

### 已实现

- [x] `CameraManager` 固定步逻辑相机 SSOT
- [x] `VirtualCameraRegistry` + `VirtualCameraRequest` 主线
- [x] profile / shot 统一为 `VirtualCameraDefinition`
- [x] priority + activation sequence 决定权威相机
- [x] 栈顶 clear 后自动回落到下一台 active virtual camera
- [x] `Cut` / `Linear` / `SmoothStep` 逻辑层镜头过渡
- [x] `CameraPresenter` 基于 `PreviousState` / `State` 的表现插值
- [x] 跟随目标解析：`LocalPlayer` / `SelectedEntity` / `SelectedOrLocalPlayer`
- [x] 轨道相机缩放 / 平移 / 旋转输入
- [x] 视锥 AABB 裁剪 + 4 级 LOD
- [x] Screen→World / World→Screen 投影工具
- [x] Camera capability mod 分层：profile / shot / bootstrap / acceptance

### 未实现

- [ ] 复杂镜头编排（Timeline / Path / Sequencer）
- [ ] 相机震动（Screen Shake）
- [ ] 相机碰撞与遮挡回弹
- [ ] 多相机/分屏
- [ ] 电影级轨道路径编辑工具

---

## 角色 (Character)

### 已实现

- [x] `WorldPositionCm` 作为唯一位置真相源
- [x] `PreviousWorldPositionCm` → `VisualTransform` 插值链
- [x] Physics2D → WorldPositionCm 直赋同步
- [x] Navigation2D 路径跟随
- [x] GAS `ForceInput2D` Sink 桥接物理
- [x] `CullState` 驱动表现层可见性
- [x] Team / PlayerOwner 身份标识

### 未实现

- [ ] 完整角色状态机
- [ ] 正式动画系统
- [ ] 角色原型装配器
- [ ] 高度 / 真 3D 角色逻辑

---

## 控制 (Control)

### 已实现

- [x] `IInputBackend` 平台抽象 + Raylib 实现
- [x] `PlayerInputHandler` context 栈
- [x] `InputRuntimeSystem` 采样 live 输入
- [x] `AuthoritativeInputSnapshotSystem` 冻结 fixed-step 快照
- [x] Camera / Input / Order 共用同一固定步输入快照
- [x] Click select / GAS response / ViewMode 切换
- [x] 交互模式、选择模式、瞄准状态机
- [x] IME 文本输入
- [x] JSON 驱动输入配置

### 未实现

- [ ] 手柄 / Gamepad 支持
- [ ] HoveredEntity 悬停检测
- [ ] 输入录制 / 回放
- [ ] 触屏适配

---

## 已知废弃项

- `CameraLogic.cs`：已被 virtual camera 主线取代
- `CameraPreset*`：已从运行时主线移除
