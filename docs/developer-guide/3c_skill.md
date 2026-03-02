# 3C 系统能力清单

## 相机 (Camera)

### 已实现

- [x] Orbit3C 轨道相机（缩放/旋转/平移）
- [x] 相机状态序列化结构（CameraState）
- [x] 控制器注册表/工厂模式（Mod 可扩展）
- [x] 球坐标→笛卡尔投影 + Lerp 平滑
- [x] 万向锁防御（Pitch ≈ 90° 切换 Up 向量）
- [x] 视锥 AABB 裁剪 + 俯仰补偿
- [x] 4 级 LOD（High / Medium / Low / Culled）
- [x] CullState 贯穿到 Performer 发射层
- [x] Screen→World 射线投射（RaylibScreenRayProvider）
- [x] World→Screen 投影（RaylibScreenProjector）
- [x] Mod 级相机配置（JSON 驱动）
- [x] 多 Mod 相机实现（Universal3C / Moba / Terrain / Benchmark）

### 未实现

- [ ] 相机动画/缓动曲线（仅线性 Lerp）
- [ ] 相机震动（Screen Shake）
- [ ] 电影镜头模式（轨道/Bezier 路径）
- [ ] 多相机/分屏
- [ ] 相机碰撞（地形遮挡回弹）
- [ ] 远裁剪面优化

---

## 角色 (Character)

### 已实现

- [x] Fix64Vec2 确定性位置（WorldPositionCm，cm）
- [x] 前帧/当前帧插值管线
- [x] Physics2D→WorldPositionCm 直赋同步
- [x] Navigation2D 路径跟随（NavGoal2D → NavArrivalSystem）
- [x] ForceInput2D Sink（GAS→物理桥接）
- [x] CullState 跳过不可见实体同步
- [x] FacingDirection 可选朝向同步
- [x] Team / PlayerOwner 身份标识

### 未实现

- [ ] 角色状态机（当前用 GameplayTag 代替）
- [ ] 动画系统（VisualModel 有 TODO）
- [ ] 角色原型构建器（手动组合组件）
- [ ] 高度/3D 位置支持（当前 2D 平面）

---

## 控制 (Control)

### 已实现

- [x] IInputBackend 平台抽象 + Raylib 实现
- [x] PlayerInputHandler（Context 栈 + 优先级）
- [x] 边缘检测（PressedThisFrame / ReleasedThisFrame）
- [x] 处理器管线（Deadzone / Normalize / Scale）
- [x] Vector2Composite（WASD→方向）
- [x] 4 种交互模式（TargetFirst / SmartCast / AimCast / Indicator）
- [x] 6 种选择类型（None / Position / Entity / Direction / Vector / Entities）
- [x] 队列/即时修饰符行为
- [x] 自动寻目标（NearestInRange / NearestEnemyInRange）
- [x] AimCast 两阶段瞄准状态机
- [x] Vector 两点向量瞄准（Origin → Direction 双阶段）
- [x] Held StartEnd 模式（按下→Start、松开→End）
- [x] IME 文本输入支持
- [x] JSON 驱动的输入/映射配置

### 未实现

- [ ] 手柄/Gamepad 支持
- [ ] Tick 级输入缓冲（IInputSource 接口已定义，零实现）
- [ ] HoveredEntity 鼠标悬停检测（回调始终 return false）
- [ ] 输入录制/回放
- [ ] 触摸屏适配

---

## 已知死代码

- `CameraLogic.cs` — 全库零引用，已被 ICameraController 模式取代
