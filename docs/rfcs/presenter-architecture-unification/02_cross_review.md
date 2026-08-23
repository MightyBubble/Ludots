# Presenter 统一编排架构交叉复核摘要

Status: Informational
Last Updated: 2026-04-16

## 1. 复核范围

本摘要汇总两位 subagent 的交叉复核意见，目标是确认：

- 用户提出的目标模型是否合理
- 当前代码是否已经具备收敛条件
- 迁移时最该避免哪些架构风险

## 2. 一致结论

两份复核结论高度一致：

1. 用户目标方向是对的
- `Presenter` 应升级为唯一演出编排真相，而不是继续只负责附加表现。

2. 当前系统不是推倒重来
- `PrefabPart`、`PrefabFinalizationPipeline`、`PresentationRequestFlushSystem` 的边界基本已经正确。

3. 真正需要收束的是双主线
- 一条是 `presenter` 编排路径
- 一条是 `entity visual` / `animator` 直通路径

4. 真正难点不是命名，而是 ownership
- 关键不在于把 `Presenter*` 改名成 `Perform*`
- 关键在于把主模型、主动画、HUD、音效、指示器统一纳入同一生命周期与可见性模型

## 3. 当前系统里已经能复用的部分

### 3.1 静态资产层

以下实现应被保留，而不是推翻：

- `src/Core/Presentation/Assets/PrefabPart.cs`
- `src/Core/Presentation/Assets/PrefabFinalizationPipeline.cs`
- `src/Core/Presentation/Assets/PrefabFinalizedVisual.cs`

复核一致认为这部分已经满足“静态、引擎面、相对稳定资产封装层”的要求。

### 3.2 现有 presenter 主骨架

以下实现已具备被提升为统一编排层的潜力：

- `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs`
- `src/Core/Presentation/Systems/PresenterRuleSystem.cs`
- `src/Core/Presentation/Systems/PresenterRuntimeSystem.cs`
- `src/Core/Presentation/Systems/PresenterEmitSystem.cs`

复核观点：

- 规则驱动
- 命令驱动
- 生命周期管理
- request 输出边界

这些骨架已经存在，说明迁移更像“收编并统一”，而不是“重做一套”。

### 3.3 低层输出 gate

以下边界建议保持不变：

- `src/Core/Presentation/Requests/PresentationRequestFlushSystem.cs`

复核一致认为 `PresentationRequest` 很适合作为 adapter-neutral flush boundary，但不应被提升为上层真相。

## 4. 当前最大不匹配

### 4.1 主视觉仍绕过 presenter

最关键的不匹配来自：

- `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`

该系统直接读取 `VisualTransform`、`VisualRuntimeState`、`PresentationStableId` 并发出 request，导致主模型并不归属于 presenter lifecycle。

### 4.2 Animator 仍归 entity visual 所有

关键路径：

- `src/Core/Presentation/Systems/AnimatorRuntimeSystem.cs`
- `src/Core/Presentation/Components/AnimatorPackedState.cs`
- `src/Core/Presentation/Components/AnimatorRuntimeState.cs`
- `src/Core/Presentation/Components/VisualRuntimeState.cs`

复核一致认为 animator 是当前最深的 ownership 问题，也是迁移中最不应仓促处理的一部分。

### 4.3 `PresentationBehavior` 还不是 runtime behavior

当前仍以 state-to-prefab resolver 为主：

- `src/Core/Presentation/Assets/PresentationBehaviorDefinition.cs`
- `src/Core/Presentation/Assets/PresentationBehaviorResolver.cs`

复核建议：

- 这部分应被收编为 `PerformBehaviorDefinition` 或过渡性 behavior definition
- 不应继续停留在“语义状态 -> prefab”的窄层次定义

## 5. 推荐架构边界

交叉复核最终收敛到以下边界：

### `Presenter`

- 唯一 orchestration SSOT
- 管理 scope、owner、anchor、stable identity、lifetime、behavior lifecycle
- 只消费上游 entity / phase 层发起的 visibility input

### `PerformRule`

- 负责匹配 event/state/phase
- 只产生命令，不直接输出 adapter 请求

### `PerformCommand`

- 一次性边沿命令
- 包含 create、destroy、set-param、activate、deactivate、pulse、play-once

### `PerformBehavior`

- 真正的持续性演出行为
- 建议分类：
  - `ModelPerformBehavior`
  - `AnimatorPerformBehavior`
  - `WorldHudPresentBehavior`
  - `IndicatorPerformBehavior`
  - `SoundPerformBehavior`
  - `VfxPerformBehavior`
  - `SplinePerformBehavior`

### `Prefab` / `PrefabPart`

- 继续只做静态资产组合与 finalization

### `PresentationRequest`

- 继续只做最终输出包与 flush gate

## 6. 关键风险

交叉复核都强调了以下风险：

1. `Presenter` 变成 god object
- 如果只是把所有逻辑塞进更大的 `PresenterEmitSystem` switch，会失去收敛意义。

2. 把 visibility truth 错放到 presenter
- 相位、可见性、观战者、debug 差异的 truth 应由 entity / phase 上游层发起，presenter 只消费这些输入。

3. 可见性策略组合爆炸
- 不能无限给 inline condition 枚举加分支，必须升级为 policy 契约。

4. 误把 `PresentationRequest` 当成架构真相
- 它只是输出层，不应承载生命周期。

5. 误把 `PrefabPart` 运行时化
- 这会破坏资产层与 runtime 层分离。

## 7. 推荐迁移顺序

交叉复核一致推荐以下顺序：

1. 锁定术语与 RFC
2. 引入 `Perform*` 并行契约
3. 统一 orchestration entry
4. 先拆 HUD / indicator / spline / VFX 行为
5. 再收编主模型
6. 最后收编动画
7. 接入 entity-fed visibility / phase input
8. 删除 legacy lane

## 8. 结论

交叉复核给出的总体判断是：

- 这套 presenter-prefab-asset 体系的底层边界并不差
- 问题不在 prefab 与 request
- 问题在 orchestration truth 仍被拆成 presenter 与 entity visual / animator 两套

因此，最合理的统一方案不是推翻当前体系，而是：

- 保留资产层与输出层
- 升级 presenter 为唯一演出编排层
- 把主模型、动画、HUD、音效、指示器统一收编进 `PerformBehavior`
