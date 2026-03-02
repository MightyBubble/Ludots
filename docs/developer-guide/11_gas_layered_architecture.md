# GAS 分层架构与 Sink 最佳实践

本篇讲清楚三件事：

*   GAS 的分层架构为什么要“分层”，以及每层负责什么。
*   分层的单一事实来源在哪里（系统组与相位）。
*   用 AttributeSink 与 Physics2D 的 sink 作为例子，给出最佳规范。

## 1 分层的单一事实来源

### 1.1 系统组是宏观分层 SSOT

GAS 的宏观分层由 `GameEngine.SystemGroup` 固化，按顺序组织：

*   AbilityActivation：能力激活与指令入口
*   EffectProcessing：效果处理主循环（含响应链）
*   AttributeCalculation：属性聚合与绑定
*   DeferredTriggerCollection：延迟触发器收集与处理

这一层的目标是：保证执行顺序可预期，避免系统之间通过“隐式顺序”耦合。

参考实现：`src/Core/Engine/GameEngine.cs` (L57-L90)

### 1.2 EffectPhase 是微观分层 SSOT

Effect 的生命周期被分解为一组 Phase（例如 OnPropose/OnResolve/OnApply/OnRemove），每个 Phase 进一步拆成：

*   Pre：轻量预处理与校验
*   Main：内建逻辑或 Graph/Preset 驱动逻辑
*   Post：收束与二次修正
*   Listeners：统一分发监听器（目标/来源/全局），按 priority 排序执行

参考实现：

*   Phase 执行器的四段式：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` (L10-L24)
*   Listeners 分发与排序：`src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs` (L170-L240)

## 2 核心链路概览

### 2.1 Ability → EffectRequest

AbilityActivation 阶段的目标是把输入/指令转成“可处理的请求”，而不是直接改世界状态。

两条典型路径：

*   `AbilitySystem`：按 AbilityDefinition 直接发布 `EffectRequest`
  参考：`src/Core/Gameplay/GAS/Systems/AbilitySystem.cs` (L51-L175)
*   `AbilityExecSystem`：按时间线/步骤执行，并在 step 中发布 `EffectRequest`
  参考：`src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs` (L513-L568)

### 2.2 Effect 主循环与响应链

EffectProcessing 不是一次性处理完所有事情，而是一个“可控的循环”，典型阶段是：

*   ProposalAndApply：提案、响应链窗口、结算与应用
*   Lifetime：持续效果 tick
*   PostLifetimeProposalAndApply：生命周期结束后的收尾处理

参考：`src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs` (L13-L160)

响应链窗口的关键点是：OnPropose 在进入响应链之前，OnCalculate 在 resolve 之后，用于把“响应修改”纳入最终结算。

参考：

*   窗口打开与 OnPropose：`src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` (L330-L376)
*   Resolve 与 OnCalculate：`src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs` (L697-L869)

### 2.3 结构变更延迟回放

Effect Apply 涉及大量结构变更（创建实体、挂组件、注册 listeners）。最佳实践是分 stage 处理并把部分操作延迟回放，避免在热路径里随意做结构改动。

参考：

*   Apply 分阶段：`src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs` (L79-L92)
*   listeners 延迟注册回放：`src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs` (L583-L628)

## 3 为什么要用 Sink

Sink 用来定义“层与层之间的边界”，把跨层的数据转换、尺度转换、写入策略集中在一个地方，避免：

*   玩法系统直接写物理组件
*   物理系统直接读取 GAS 的临时属性缓冲
*   float 与 Fix64 的混用扩散到所有系统

在 Ludots 中，sink 通常由两部分组成：

*   `AttributeSinkRegistry`：注册 sink 并冻结 ID，防止运行时漂移
*   `AttributeBindingSystem`：按 sink 分组执行 Apply，把“属性缓冲”落地到具体层

参考：

*   Registry：`src/Core/Gameplay/GAS/Bindings/AttributeSinkRegistry.cs`
*   Binding loader：`src/Core/Gameplay/GAS/Bindings/AttributeBindingLoader.cs` (L23-L125)
*   Binding apply：`src/Core/Gameplay/GAS/Systems/AttributeBindingSystem.cs` (L18-L28)

## 4 最佳实践示例一：Attribute 分层 Sink

### 4.1 规范目标

Attribute 层的“输出”应当通过 sink 落地，而不是在 effect phase 里到处 `world.Add/Set`：

*   effect phase 负责计算与写入“属性缓冲”
*   AttributeCalculation 阶段统一落地到目标层（例如物理、导航、UI 可见状态）

### 4.2 规范写法

1.  定义 sink 的名字与实现（集中放在 Bindings）
2.  在一个“注册点”统一 Register（并在最终 Freeze）
3.  用 `GAS/attribute_bindings.json` 声明属性与 sink 的绑定关系
4.  让 `AttributeBindingSystem` 在 AttributeCalculation 阶段执行所有 sink

## 5 最佳实践示例二：Physics2D 的分层 Sink

`ForceInput2DSink` 是一个典型的“跨层边界”实现：

*   输入侧：从 AttributeBuffer（float 域）读取力/方向
*   转换：把 float 转成 Fix64Vec2，并做每逻辑帧 reset（可选）
*   输出侧：写入物理层组件 `ForceInput2D`，供 Physics2D 在自己的时钟域消费

参考：

*   sink 实现：`src/Core/Gameplay/GAS/Bindings/ForceInput2DSink.cs`
*   内建 sink 注册：`src/Core/Gameplay/GAS/Bindings/GasAttributeSinks.cs` (L3-L13)

### 5.1 为什么这是最佳规范

*   类型/尺度转换集中：float→Fix64 的边界不扩散到其他系统。
*   写入策略集中：reset-per-logic-frame 这种策略不污染玩法逻辑。
*   时钟域解耦：物理 tick 频率与 GAS 固定 tick 可以不同步，由 controller 负责对齐。

Physics2D 的时钟域与事件出口由 `Physics2DController` 管理，并通过 TriggerManager 提供可观测事件出口。

参考：

*   controller：`src/Core/Engine/Physics2D/Physics2DController.cs`
*   controller 注入 FireEvent：`src/Core/Engine/GameEngine.cs` (L603-L608)

## 6 约束清单

*   结构变更集中在明确的阶段或队列回放，不在热路径里随意做结构改动。
*   跨层写入必须有边界：优先用 sink，而不是让系统直接写入对方层的数据。
*   sink 的名称与 ID 是契约：注册集中化并冻结，避免运行时漂移。
*   effect phase 不应依赖某个 sink 的执行顺序；顺序由 SystemGroup 固化。

