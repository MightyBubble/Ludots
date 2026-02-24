---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 交互层 - 表现层 - Animator
状态: 草案
---

# Tag驱动Animator分层 架构设计

# 1 设计概述
## 1.1 本文档定义
本文档定义一套完全以 Tag 驱动为主的 Animator 状态机系统，并明确三层分离：

- 状态机基建：通用 Layer + condition + resolver，可被动画以外系统复用。
- 动画播放接口层：平台无关的播放快照与资源键。
- 平台适配层：把播放快照落到 Raylib/Unity/Web 等具体实现。

本文档同时约束“配置需要动画的表演者，就有动画组件；没配的就没有”，以保证内存紧凑与查询开销可控。

非目标：

- 不定义骨骼蒙皮的具体数据格式与 GPU 路径。
- 不定义编辑器工具链的具体 UI，只定义产物契约。

## 1.2 设计目标
- 状态即 Tag：Animator 的状态标识直接使用 TagId。
- 0GC：运行期只做数组访问、少量位运算，不分配。
- 强边界：播放接口层不暴露平台对象句柄；平台层不读取玩法真源。
- 易于 LOD/Culling/AOI 管控：Animator 的更新频率与可见性口径统一接入 `CullState`。

## 1.3 设计思路
- Animator 本质是“为某些表演者实体提供一个可选能力组件集合”。\n
- 运行期分为两段：\n
  - 输入采样：将 Attribute/速度等连续量写入 ParamBlock。\n
  - 状态求解：用通用状态机 Resolver 基于 TagMask+ParamBlock 求得每层状态，再映射为播放快照。\n
- 输出对平台只给快照，不给实现细节。

# 2 功能总览
## 2.1 术语表
- 表演者：带 `VisualModel/VisualTransform` 且参与表现输出的实体。
- 状态 Tag：被约定为“状态集合成员”的 Tag（按 layer 划分候选集）。
- Param：连续量参数，统一以 ParamId 索引；由输入采样系统写入。
- ClipKey：平台无关的动画片段键（int），由平台注册表解析为真实 clip。
- PlaybackSnapshot：每帧输出的播放快照项，由平台适配层消费。

## 2.2 功能导图
- 输入：`GameplayTagEffectiveCache`、ParamBlock、`CullState`。
- 处理：按 layer 选状态 Tag、评估 condition、优先级选优、推进时间与 blend。
- 输出：`AnimationPlaybackSnapshotBuffer`（平台消费）。

## 2.3 架构图
```text
实体(表演者)
  VisualModel + VisualTransform + CullState
  + AnimatorBinding + AnimatorRuntime + ParamBlock
        |
        | ParamFromAttribute
        v
  ParamBlock(定长float)
        |
        | Resolver(TagMask + Param)
        v
  AnimatorRuntime(每层ActiveStateTag/Time)
        |
        | SnapshotBuild
        v
  AnimationPlaybackSnapshotBuffer
        |
        v
平台适配层播放
```

## 2.4 关联依赖
- 通用状态机基建：`docs/06_交互层/14_表现层/03_可复用状态机基建.md`
- Tag 有效缓存：`src/Core/Gameplay/GAS/Components/GameplayTagEffectiveCache.cs`
- 可见性裁剪：`src/Core/Presentation/Components/CullState.cs`、`src/Core/Systems/CameraCullingSystem.cs`
- 表现主循环：`src/Core/Engine/GameEngine.cs`

# 3 业务设计
## 3.1 业务用例与边界
用例：

- 角色 locomotion：Idle/Walk/Run 状态由 Tag 驱动，速度参数作为门控。
- 上半身叠加：Aim/Carry 等作为 overlay layer，通过 Tag + 参数权重混合。
- 受击反应：可作为 one-shot layer 的状态 Tag，由 Tag 变化或 TagPulse 触发。

边界：

- Animator 不负责产生 Tag（Tag 来自玩法裁决或工具配置）。
- Animator 不反向写玩法属性或 Tag。

## 3.2 业务主流程
```text
每帧 Visual Loop
  -> 若实体有 AnimatorBinding:
       1) 采样 Attribute/速度写 ParamBlock
       2) 读取 Effective TagMask
       3) Resolver 选出每层 ActiveStateTagId
       4) 推进时间与 blend
       5) 写 PlaybackSnapshotBuffer
  -> 平台消费快照播放
```

## 3.3 关键场景与异常分支
- 未配置动画：实体没有 AnimatorBinding 时，完全不参与 Animator 查询与输出。
- 缺少资源映射：ClipKey 必须能在加载期解析到平台资源；缺失时 fail-fast，禁止运行时静默降级。
- 条件冲突：多个状态 Tag 同时满足时，以 Priority 与 TagId 稳定 tie-break。

# 4 数据模型
## 4.1 概念模型
- AnimatorGraph：按 layer 划分状态 Tag 候选集与每个状态的 condition、优先级与 clip 映射。
- ParamRegistry：将参数名映射为 ParamId，运行期只使用 ParamId。
- ParamBinding：参数来源绑定到 AttributeId，加载期编译成 AttrIndex。
- PlaybackSnapshot：实体在某时刻应该播放的 clip、时间、权重与遮罩信息。

## 4.2 数据结构与不变量
### 4.2.1 状态即 Tag
- ActiveStateId 直接使用 TagId，状态机基建的 StateId 与 TagId 同构。
- 每个 layer 的候选集用 TagMask（4×ulong）表达，避免 per-state 列表分配。

### 4.2.2 Condition 预设类
沿用通用状态机基建的三类 condition：

- ParamCondition：比较 ParamBlock 中的 float 值。
- TagCondition：检查单 Tag 的 Effective/Present。
- TagMaskCondition：HasAll/HasAny/ExcludeAny（基于 EffectiveCache 位集）。

### 4.2.3 Param 输入源 AttributeId 绑定
- 绑定资产描述 `ParamId <- AttributeId`，加载期编译为 `ParamId <- AttrIndex`。
- 运行期采样系统只做数组读取与写入，不做查字典。

### 4.2.4 PlaybackSnapshot 契约
推荐最小字段集：

- EntityId
- AnimationSetId
- ClipKeyId
- TimeTicks
- Weight
- LayerId
- BlendMode
- MaskId

不变量：

- 平台消费快照时不允许读取玩法组件，只依赖快照与资源注册表。
- SnapshotBuffer 为固定容量，满载时丢弃必须可观测。

## 4.3 生命周期/状态机
- AnimatorRuntime 是持久状态：只要实体活着且绑定 Animator，就持续存在。
- 不可见/低 LOD 的推进策略由裁决条款统一规定，避免“各系统各读各的信号”。

# 5 落地方式
## 5.1 模块划分与职责
- 状态机基建模块：提供 GraphCompiler 与 Resolver。
- Animator 输入采样：ParamFromAttributeSystem，把 Attribute/速度写入 ParamBlock。
- Animator 求解与推进：Resolve/Advance 系统更新 AnimatorRuntime。
- Animator 输出：SnapshotBuild 系统写 AnimationPlaybackSnapshotBuffer。
- 平台适配：消费 SnapshotBuffer 并播放。

## 5.2 关键接口与契约
- Graph 必须可在加载期 Freeze：引用的 TagId/ParamId/ClipKey 必须存在。
- ParamRegistry 与 TagRegistry 的注册必须集中化，禁止业务代码散写 magic string。
- SnapshotBuffer 的语义必须与 DrawBuffer 一致：只读消费、帧内清空。

## 5.3 运行时关键路径与预算点
- Resolver 成本：候选状态 Tag 数量 × condition 数量。每层候选集必须有上限并可诊断。
- Param 采样成本：绑定条目数量 × 读取 attribute 次数。绑定条目必须可控。
- LOD 降频策略：低 LOD 可跳帧或只推进时间不重选状态，必须有统一口径与统计。

# 6 与其他模块的职责切分
## 6.1 切分结论
- Animator 只读玩法裁决输入（Tag/Attribute），不写入玩法真源。
- 可见性/LOD/AOI 的信号由空间服务与 culling 系统提供，Animator 只读并按裁决条款执行降级策略。
- 瞬态表演（例如一帧触发的短动画）可按需走 PerformerRuntime 或 TagPulse，不强制实体化。

## 6.2 为什么如此
- “配了才有”用组件存在性表达，天然符合 ECS 查询与内存紧凑。
- 平台层只读快照，避免跨层依赖与难以复用的渲染耦合。

## 6.3 影响范围
- 后续若引入骨骼资源与遮罩，建议新增 AnimationAssetRegistry，但不应改变 SnapshotBuffer 的消费口径。

# 7 当前代码现状
## 7.1 现状入口
- Visual Loop：`src/Core/Engine/GameEngine.cs`
- Tag Effective Cache：`src/Core/Gameplay/GAS/Components/GameplayTagEffectiveCache.cs`
- 表现缓冲模式：`src/Core/Presentation/Rendering/PrimitiveDrawBuffer.cs`

## 7.2 差距清单
- Core 侧缺少 AnimatorGraph、ParamRegistry/Binding、SnapshotBuffer 以及对应系统。
- 平台侧缺少 AnimationPlaybackSnapshot 的消费实现与资源注册表。

## 7.3 迁移策略与风险
- 先以“状态即 Tag + 少量参数门控 + Raylib 侧快照可视化”建立闭环，再引入真实骨骼/clip。

# 8 验收条款
- 配置隔离：未配置 AnimatorBinding 的实体不会进入 Animator 查询，也不会产生任何播放快照输出。
- 0GC：1 万实体场景下 Animator 不产生托管分配，且每层候选集与绑定条目有明确上限。
- 一致口径：Animator 的可见性/降级策略只读取 `CullState` 并遵循裁决条款，不允许私自引入第二信号源。
