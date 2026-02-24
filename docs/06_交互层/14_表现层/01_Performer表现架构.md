---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 交互层 - 表现层 - Performer
状态: 草案
---

# Performer表现架构 架构设计

# 1 设计概述
## 1.1 本文档定义
本文档定义 Ludots 表现层的 Performer 架构：从玩法事件与 Tag 变化，到表现命令、运行时管理，再到平台侧渲染消费的完整链路与边界。

本文档同时给出“持久表演者”和“瞬态表演”的两种形态如何共存，以及它们与可见性裁剪、LOD、AOI 的集成口径。

非目标：

- 不定义具体平台的渲染实现细节（Raylib/Unity/Web）。
- 不在本文实现 Animator 或骨骼动画，仅定义与其对接的表现管线契约。

## 1.2 设计目标
- 单向数据流：玩法真源只读，表现层不反向裁决玩法。
- 可丢帧可降级但可观测：容量溢出、跳帧、降频必须显式统计与可诊断。
- 0GC 热路径：事件/命令/渲染缓冲均为固定容量结构，运行时不分配。
- Arch ECS 对齐：系统以“查询组件或消费缓冲→写入缓冲”为主，不把业务状态藏进不可观察的全局黑箱。

## 1.3 设计思路
将表现层拆成四段职责，保持每段都可替换、可复用、可做预算控制：

- 事件桥接：从玩法域收集事件与 Tag 变化，写入表现事件流。
- 控制映射：从表现事件映射为可执行的表现命令。
- 运行时管理：消费表现命令，维护瞬态表演的生命周期/跟随/淡出等行为。
- 渲染汇总：把运行时结果写入平台无关的 DrawBuffer，由平台适配层统一消费。

# 2 功能总览
## 2.1 术语表
- 表演者：能产生可视输出的对象。在 Ludots 当前代码中，通常表现为带 `VisualTransform` 的实体或由瞬态 Runtime 维护的实例。
- 持久表演者：生命周期与玩法实体一致或跨越多帧长期存在的表演者。
- 瞬态表演：短命、数量大、一次性触发的表演（例如伤害提示、爆点）。
- 表现事件：表现层可消费的事件数据，入口为 `src/Core/Presentation/Events`。
- 表现命令：表现运行时可执行的命令，入口为 `src/Core/Presentation/Commands`。
- DrawBuffer：平台侧渲染消耗的批量数据缓冲，例如 `PrimitiveDrawBuffer`。

## 2.2 功能导图
- 输入：GameplayEvent、TagEffectiveChanged、相机与可见性信号。
- 处理：事件汇聚、命令映射、生命周期管理、锚点跟随、alpha/缩放曲线。
- 输出：PrimitiveDrawBuffer、WorldHudBatchBuffer、后续可扩展的 AnimationPlaybackSnapshotBuffer。

## 2.3 架构图
```text
玩法域
  GameplayEventBus / Tag系统
        |
        v
表现域
  PresentationEventStream
        |
        v
  PresentationControlSystem
        |
        v
  PresentationCommandBuffer
        |
        v
  PerformerRuntimeSystem(瞬态)
        |
        v
  PrimitiveDrawBuffer / 其他DrawBuffer
        |
        v
平台适配层
  Raylib/Unity/Web Renderer
```

## 2.4 关联依赖
- 事件桥接：`src/Core/Presentation/Systems/PresentationBridgeSystem.cs`
- 控制映射：`src/Core/Presentation/Systems/PresentationControlSystem.cs`
- 瞬态运行时：`src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`
- 基础缓冲：`src/Core/Presentation/Events/PresentationEventStream.cs`、`src/Core/Presentation/Commands/PresentationCommandBuffer.cs`
- 渲染缓冲：`src/Core/Presentation/Rendering/PrimitiveDrawBuffer.cs`
- 可见性裁剪：`src/Core/Systems/CameraCullingSystem.cs`、`src/Core/Presentation/Components/CullState.cs`
- 坐标同步：`src/Core/Systems/GridVisualSyncSystem.cs`

# 3 业务设计
## 3.1 业务用例与边界
用例：

- 伤害/治疗等 GameplayEvent：在目标位置播放一次性提示（marker、文字、短动画）。
- Tag 生效/失效：在表演者身上触发持续或一次性视觉反馈（例如状态图标、光环）。
- 调试与开发：以低成本输出可视化信息，帮助定位玩法事件流与 Tag 变化。

边界：

- 表现层不决定“事件是否有效”，只消费玩法域的裁决结果。
- 表现层可选择性跳过不可见对象或低 LOD 对象的更新。

## 3.2 业务主流程
```text
GameplayEvent/Tag变化
  -> Bridge 写 PresentationEvent
  -> Control 将 Event 映射为 Command
  -> Runtime 消费 Command，生成表演实例
  -> Runtime 写入 DrawBuffer
  -> 平台消费 DrawBuffer 渲染
```

## 3.3 关键场景与异常分支
- 缓冲溢出：EventStream/CommandBuffer/DrawBuffer 满时必须丢弃并累计 dropped 统计，禁止静默吞掉不留痕迹。
- 锚点跟随：瞬态表演可跟随目标实体的 `VisualTransform`，若锚点不可用则退化为静态位置并可观测。
- 不可见对象：对 `CullState.IsVisible=false` 的对象，允许冻结表现状态或降频更新，但必须有统一口径。

# 4 数据模型
## 4.1 概念模型
- PresentationEvent：玩法域到表现域的统一事件形态。
- PresentationCommand：表现策略层对运行时的指令形态。
- PerformerInstance：瞬态表演的运行时实例（生命周期、颜色、跟随信息）。
- DrawItem：平台渲染消耗的最小项（例如 PrimitiveDrawItem）。

## 4.2 数据结构与不变量
- 固定容量：EventStream、CommandBuffer、DrawBuffer 都是固定容量数组，`TryAdd` 失败必须记录 dropped。
- 单向消费：事件与命令在同一帧被消费后必须 `Clear`，禁止重复消费导致表现重放。
- 参数槽位：命令的 `Param0/1/2` 为通用槽位，必须在文档或注册表中定义语义，禁止随意复用。

## 4.3 生命周期/状态机
- 瞬态表演：由 Runtime 维护 `Lifetime/TimeLeft`，结束后 O(1) 移除。
- 持久表演者：按需使用“实体组件驱动”承载长期状态，适合被 AOI/LOD/Culling 统一管理。

# 5 落地方式
## 5.1 模块划分与职责
- `PresentationBridgeSystem`：把玩法事件与 Tag 变化汇聚为表现事件。
- `PresentationControlSystem`：把表现事件映射成表现命令，承载“如何表演”的策略。
- `PerformerRuntimeSystem`：承载瞬态表演的生命周期与跟随逻辑。
- `*DrawBuffer`：承载跨平台的渲染输入。
- 平台适配层：只做“消费 DrawBuffer 并渲染”，不读玩法真源。

## 5.2 关键接口与契约
- 缓冲统一契约：`TryAdd / GetSpan / Clear`，并带 `DroppedCount` 或等价指标。
- 命令与资源映射：`IdA/IdB` 必须通过注册表解析，禁止业务代码写 magic id。

## 5.3 运行时关键路径与预算点
- Bridge：O(事件数 + Tag变更数)，必须可控上限。
- Control：O(表现事件数)，映射逻辑必须分支少、无分配。
- Runtime：O(命令数 + 实例数)，实例数组上限决定 worst-case。
- DrawBuffer：平台侧遍历 O(DrawItem 数)，应支持 instancing 分组。

# 6 与其他模块的职责切分
## 6.1 切分结论
- 可见性与 LOD：由 `CullState` 作为统一信号源；Performer/Animator 只读取，不写入裁决。
- AOI：负责“哪些对象应被加载/激活”，不负责具体渲染。
- 相机：提供视锥与投影参数，不参与表现策略。

## 6.2 为什么如此
- 单一信号源减少“各系统各读各的”导致的行为不一致与难以诊断。
- 将策略放在 Control 层，有利于统一配置与替换，不污染 Runtime 的可复用性。

## 6.3 影响范围
- 若未来引入动画播放快照缓冲，应保持与 DrawBuffer 相同的消费语义，平台侧仍然只读缓冲。

# 7 当前代码现状
## 7.1 现状入口
- 事件桥接：`src/Core/Presentation/Systems/PresentationBridgeSystem.cs`
- 控制映射：`src/Core/Presentation/Systems/PresentationControlSystem.cs`
- 瞬态运行时：`src/Core/Presentation/Systems/PerformerRuntimeSystem.cs`
- 可见性裁剪：`src/Core/Systems/CameraCullingSystem.cs`

## 7.2 差距清单
- 缺少通用状态机基建，导致 Animator/AI/UI 等无法复用同一套 tag+param+condition+layer 口径。
- 缺少动画播放接口层与平台适配层的统一契约。
- `VisualTransform` 在 culled 时停更会影响“锚点跟随型瞬态表演”的位置一致性，需要在裁决条款中明确期望行为。

## 7.3 迁移策略与风险
- 瞬态表演保持现有 Runtime 模式，不强行实体化，优先补齐口径与可观测性。
- 长期表演者以实体组件驱动为主，避免把长期状态藏进 Runtime 私有数组导致不可观察与难以被 AOI 管控。

# 8 验收条款
- 0GC：在稳定场景下每帧不产生托管分配，验证入口为 profiler/GC 统计与关键系统的基准用例。
- 可观测：Event/Command/DrawBuffer 满载时必须可观察 dropped 统计，且能定位来源缓冲。
- 单向依赖：表现系统不允许写入玩法裁决数据，检查入口为系统职责清单与代码审计。
