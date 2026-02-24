---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 生命周期 Phase 管线
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
---

# Effect 生命周期 Phase 架构设计

本文描述"一个 effect 自身到了什么阶段做什么"——即 Phase 管线的架构。

反应式行为（"别的 effect 到达某 phase 时，我做什么"）见 `07_EffectPhaseListener_架构设计.md`。

# 1 8 Phase 生命周期

```text
OnPropose → (ResponseChain) → OnCalculate → OnResolve → OnHit → OnApply → OnPeriod → OnExpire → OnRemove
    0                             1              2          3        4          5          6          7
```

| Phase | 触发时机 | 典型用途 |
|---|---|---|
| OnPropose | Effect 进入 Proposal 窗口 | 预检查、标记、UI 预览 |
| OnCalculate | ResponseChain 结算后 | 计算最终 modifier 值、应用公式 |
| OnResolve | 目标搜索 | 空间查询、Graph 查询收集候选目标 |
| OnHit | 逐目标命中验证 | 闪避、护盾、免疫检测 |
| OnApply | 施加 Modifier | 实际修改属性、触发子效果 |
| OnPeriod | 周期 tick | DOT 伤害、HOT 治疗 |
| OnExpire | 自然过期 | 清理、触发过期效果 |
| OnRemove | 强制移除 | 驱散、清理 |

**哪些 Phase 活跃由 PresetType 定义表声明**——不是所有 effect 都走全部 8 个 Phase。Instant 类型不走 OnPeriod/OnExpire/OnRemove，不带 TargetQuery 的类型不走 OnResolve。

> **纯 Instant 优化路径注意事项**：纯 Instant 效果（无 PhaseGraphBindings、无 ListenerSetup、无 TargetResolver、无副作用 PresetType）不创建 effect entity，modifiers 内联 apply。但 **OnApply Phase Listeners 仍然触发**——通过 `EffectPhaseExecutor.DispatchPhaseListeners` 在内联 apply 后显式分发。这确保了全局/实体 Phase Listener 的可观察性不会因优化路径而丢失（例如"每当受到伤害时"类触发器）。
>
> 如果纯 Instant 效果需要 OnApply 的 Pre/Post Graph 执行，应在模板上配置 `phaseGraphs`，此时 `IsPureInstantTemplate` 返回 false，走完整的 entity-based Phase 管线。

# 2 三段式执行模型

每个活跃 Phase 内部按 **Pre → Main → Post** 三段执行：

```text
     ┌─────────┐     ┌─────────┐     ┌─────────┐     ┌────────────┐
     │ Pre     │────>│ Main    │────>│ Post    │────>│ Listeners  │
     │ (Graph) │     │ (C#/Gr) │     │ (Graph) │     │ (dispatch) │
     └─────────┘     └─────────┘     └─────────┘     └────────────┘
```

| Slot | 来源 | 配置位置 | 说明 |
|---|---|---|---|
| **Pre** | 用户 per-template 配置 | `phaseGraphs.{phase}.pre` | 前置 Graph，可修改 Blackboard/寄存器 |
| **Main** | PresetType 默认处理器 | `PresetTypeDefinition.PhaseHandlers` | 核心逻辑：C# 回调或 Graph，执行路径等价 |
| **Post** | 用户 per-template 配置 | `phaseGraphs.{phase}.post` | 后置 Graph，可补充逻辑 |
| **Listeners** | 其他 effect 注册的监听 | 见 `07_EffectPhaseListener` | Phase 执行完毕后分发，独立于 Pre/Main/Post |

**SkipMain**：可在 `phaseGraphs` 中设置 `"skipMain": true`，跳过预设 Main，由 Pre/Post Graph 完全接管。

## 2.1 EffectPhaseGraphBindings

存储 per-template 的 Pre/Post Graph 绑定（重命名自 `EffectBehaviorTemplate`）。

```csharp
// struct 重命名：EffectBehaviorTemplate → EffectPhaseGraphBindings
public struct EffectPhaseGraphBindings
{
    // 每个 Phase 的 (Pre, Post, SkipMain) 三元组
    // 实现为固定大小数组，按 PhaseId 索引
}
```

对应 JSON 字段 `phaseGraphs`：

```json
"phaseGraphs": {
  "onCalculate": {
    "pre": "Graph.DamageFormula.Standard",
    "post": "Graph.DamageBonus.ElementalCheck",
    "skipMain": false
  }
}
```

**校验**：`phaseGraphs` 中的 phase 名必须在该 PresetType 的 `activePhases` 中，否则 Loader 告警。

# 3 预设行为的实现模式

**和虚幻引擎的类比**：

| 虚幻引擎 | Ludots |
|---|---|
| C++ 原生实现 | C# `ApplyPresetModifiers` 等硬编码逻辑 |
| Blueprint 蓝图覆盖 | `PresetTypeDefinition.PhaseHandlers` 注册的 Main Graph |
| Blueprint 事件绑定 | `EffectPhaseGraphBindings` 的 Pre/Post Graph |
| C++ 中调用 Blueprint 虚函数 | Phase 执行器依次调用 Pre → Main → Post |

**关键原则**：C# 硬编码是合法的、甚至是首选的预设行为实现方式。Graph 是可选的配置化扩展路径，不是 C# 的替代品。

# 4 Graph 无状态原则

Graph 程序**不持有任何状态**。每次执行时，寄存器从零初始化。参数必须有明确的存储归属：

| 效果类型 | 参数存储位置 | 访问方式 |
|---|---|---|
| 持续效果 (After/Infinite) | effect entity 的 Blackboard 组件 | `ReadBlackboardFloat/Int` |
| 瞬时效果 (Instant) | context entity（Source/Target）的 Blackboard | `ReadBlackboardFloat/Int` |
| 模板级参数 | EffectTemplateData.ConfigParams | `LoadConfigFloat/Int` |
| 实例级覆盖 | EffectCallerParams 组件 (merged into ConfigContext) | `LoadConfigFloat/Int` |
| 运行时属性 | 目标 entity 的 AttributeBuffer | `LoadAttribute` |

**注意**：Blackboard 写入是即时的——同一 Phase 的 Pre/Main/Post 共享寄存器状态和 Blackboard，Post 可以读到 Pre 写入的值。

# 5 EffectBehaviorTemplate 重命名

`EffectBehaviorTemplate` 是误导性命名，它实际上只是一个 **(Phase, Slot, GraphId) 映射表**，不代表"行为"。

**改造**：重命名为 `EffectPhaseGraphBindings`。对应的 JSON 字段从概念上的 "behavior" 改为 `phaseGraphs`。代码侧的 struct 重命名在 Phase A 中执行。
