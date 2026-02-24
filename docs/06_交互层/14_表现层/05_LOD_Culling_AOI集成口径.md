---
文档类型: 裁决条款
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 交互层 - 表现层 - LOD与可见性
状态: 草案
---

# LOD与可见性裁剪裁决条款

# 1 裁决背景
## 1.1 背景问题与约束
表现层涉及多类系统：可见性裁剪、Transform 同步、HUD 收集、瞬态表演、未来的 Animator 播放。若各系统各自定义“可见/不可见”“低 LOD 是否更新”的口径，将导致：

- 行为不一致：HUD 停更但特效继续跑，或动画推进但锚点位置冻结。
- 难以诊断：读者无法从单一信号源判断当前表现为何停更。
- 性能不可控：某些系统绕开 culling 信号持续更新，形成隐性开销。

现有代码已引入 `CullState` 并在多个系统中使用，但仍存在需要明确口径的交互点：

- `GridVisualSyncSystem` 在 `CullState.IsVisible=false` 时不更新 `VisualTransform`，见 `src/Core/Systems/GridVisualSyncSystem.cs`。
- 瞬态表演可跟随锚点 `VisualTransform`，见 `src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`。
- AOI 基建存在但尚未形成统一接入路径，见 `src/Core/Navigation/AOI/HexGridAOI.cs`。

## 1.2 适用范围与边界
适用范围：

- 所有读取 `VisualTransform/VisualModel` 并输出 DrawBuffer/PlaybackSnapshot 的表现系统。
- 未来所有 Animator、VFX、HUD、DebugDraw 等表现输出系统。

边界：

- 本裁决不改变玩法裁决与 AOI 的业务含义，只规定表现层如何消费信号。

# 2 裁决结论
## 2.1 必须遵守的规则
规则 1 单一可见性信号源

- 表现层的“可见/不可见/LODLevel”必须以 `CullState` 为唯一信号源。
- 禁止在表现系统内部自建第二套可见性判定（除非写在例外条款并提供退出条件）。

规则 2 统一的更新策略表

- 每个表现系统必须明确其在以下三种情况下的行为，并保持一致：\n
  - `CullState.IsVisible=true`\n
  - `CullState.IsVisible=false`\n
  - `CullState.LODLevel=Low/Medium/High`（或等价分档）\n
- 推荐基线策略：\n
  - IsVisible=false：允许停止重计算与停止推进时间，表现冻结；\n
  - 低 LOD：允许跳帧或降频重计算，但必须可观测（计数/日志级别可控）。\n

规则 3 VisualTransform 的语义必须清晰

- `VisualTransform` 语义为“渲染侧 Transform”，允许在不可见时停更。\n
- 任何需要“不可见时仍精确跟随”的系统不得依赖 `VisualTransform`；应改用玩法域位置组件或显式的“AnchorPosition”输入。\n
- 若系统仍选择依赖 `VisualTransform`，则其在不可见时的位置冻结必须视为预期行为并写入系统文档。\n

规则 4 瞬态表演的锚点跟随退化口径

- 瞬态表演跟随锚点时，以“锚点的 `VisualTransform` 是否更新”为准。\n
- 当锚点不可见导致 `VisualTransform` 停更时，瞬态表演允许冻结在最后位置，不要求额外补偿。\n
- 若某类瞬态表演必须在不可见时仍精确跟随，只允许通过“改为持久表演者组件驱动”或“改用玩法位置作为锚点”两种方式解决。\n

规则 5 AOI 只负责激活与加载，不负责渲染判定

- AOI 的职责是决定哪些对象应被加载/激活到本客户端或本视域的工作集。\n
- 渲染级的可见性裁剪仍以 `CullState` 为准。\n
- AOI 退场时允许批量停用表现更新（等价于统一设置为不可见），但必须有明确的状态标识或统计入口。\n

## 2.2 例外条件与退出条件
例外 1 平台侧可见性信号

- 若平台渲染后端提供更精确的可见性信号（例如 GPU occlusion），允许作为附加优化信号使用。\n
- 退出条件：一旦平台侧信号无法稳定提供，必须能回退到仅使用 `CullState` 的基线策略，且行为可解释。\n

例外 2 极低成本的 debug 输出

- DebugDraw/HUD 若在低 LOD 下仍更新，可作为开发期例外。\n
- 退出条件：发布构建必须关闭该例外或降级到基线策略。\n

# 3 裁决理由
## 3.1 为什么这样做
- 单一信号源让性能预算与行为诊断可落盘：读者只看 `CullState` 就能推断表现系统是否应运行。\n
- `VisualTransform` 作为渲染 transform 的语义明确后，系统不会再误用它承担玩法定位职责。\n
- AOI 与 culling 职责分离能避免“对象不在 AOI 但仍被渲染/更新”的隐性 bug。\n

## 3.2 放弃了哪些备选方案
- 方案 A 每个系统自建可见性判定：放弃，原因是口径分叉导致不可控。\n
- 方案 B 强制所有瞬态表演实体化：放弃，原因是结构变更与管理成本上升，且与“按需”原则冲突。\n

# 4 影响范围
## 4.1 受影响模块与代码入口
- 可见性裁剪：`src/Core/Systems/CameraCullingSystem.cs`\n
- 可见性状态：`src/Core/Presentation/Components/CullState.cs`\n
- Transform 同步：`src/Core/Systems/GridVisualSyncSystem.cs`\n
- 瞬态表演：`src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`\n
- AOI 基建：`src/Core/Navigation/AOI/HexGridAOI.cs`\n

## 4.2 迁移与过渡策略
- 新增表现系统时必须显式声明其对 `CullState` 的策略（冻结/降频/继续推进），并在验收条款中给出可观测指标。\n
- 对现有系统先以文档为准，不强行改实现；若观察到因 `VisualTransform` 停更导致的表现不一致，再按规则 3 的两条合规路径选择修正方案。\n
