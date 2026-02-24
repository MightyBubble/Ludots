---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 模板与实例的边界
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/05_Effect参数架构_架构设计.md
---

# Effect 模板与实例 架构设计

# 1 Template（共享，注册表存储）

`EffectTemplateData` 存储在 `EffectTemplateRegistry` 中，加载后不可变。

| 字段 | 生命周期 | 被谁读取 |
|---|---|---|
| 结构参数 (Duration/Period/Clock) | 编译期固化 | `EffectApplicationSystem` 创建 entity 时 |
| ConfigParams | 编译期固化 + 注入结构参数 | 每次 Phase Graph 执行前 `SetConfigContext` |
| PhaseGraphBindings | 编译期固化 | `EffectPhaseExecutor` 查 Pre/Post Graph |
| ListenerSetup | 编译期固化 | `EffectApplicationSystem` 在 OnApply 后注册 |
| TargetQuery/Filter/Dispatch | 编译期固化 | `TargetResolverFanOutHelper` 三阶段执行 |
| Modifiers | 编译期固化（默认值） | `EffectProposalProcessingSystem` 复制到 proposal |

**关键特性**：同 template 的所有活跃 effect 实例在每次 Phase 执行时从 template 读取 ConfigParams 和 PhaseGraphBindings。**修改 template 会影响所有实例**。

# 2 Instance（per-entity，effect entity 上）

| 组件 | 内容 | 可变性 |
|---|---|---|
| `GameplayEffect` | LifetimeKind, ClockId, TotalTicks, RemainingTicks, PeriodTicks, ExpiresAtTick, State | 运行时递减（LifetimeSystem 管理） |
| `EffectModifiers` | 属性修改器列表 | Proposal 阶段可被 ResponseChain 修改，OnApply 后固化 |
| `EffectContext` | RootId, Source, Target, TargetContext | 创建后不变 |
| `EffectCallerParams`（新增） | per-instance 参数覆盖 | 创建后不变（caller 的快照） |
| `EffectTemplateRef` | TemplateId 引用 | 创建后不变 |
| `BlackboardFloatBuffer` | Graph 运行时读写的 float 状态 | 随时读写 |
| `BlackboardIntBuffer` | Graph 运行时读写的 int 状态 | 随时读写 |
| `EffectPhaseListenerBuffer` | 已注册的 phase listeners | OnApply 时注册，OnExpire/OnRemove 时注销 |

# 3 升级机制

## 3.1 三层 Merge 模型

```text
Template ConfigParams (基准值)
        ↑ template 可被热更新 → 所有实例的下次 phase 执行读到新值
EffectCallerParams (实例覆盖, caller wins)
        ↑ 创建后不变
─────────────────────────────────
最终值 = Merge(Template.ConfigParams, Entity.CallerParams)
```

## 3.2 用例

**Buff 升级（修改 template）**：
1. DOT Burn 模板的 `configParams.tickDamage` 默认为 5。
2. 技能系统升级 Burn 模板，将 `tickDamage` 改为 8。
3. 所有活跃的 Burn effect 实例在下次 OnPeriod 执行时，`LoadConfigFloat("tickDamage")` 返回 8（从 template 读取）。
4. 但某个实例有 CallerParams 覆盖 `tickDamage=15`，该实例仍然读到 15（caller wins）。

**运行时 template 修改**：当前 `EffectTemplateRegistry` 支持 `Register()` 覆盖已有 template，但无通知机制。如果需要安全的 template 升级：
- 方案 A：直接覆盖 template（简单，但无法追溯哪些实例受影响）
- 方案 B：引入版本号，实例记录创建时的版本（复杂，当前不实施）

**当前建议**：采用方案 A（直接覆盖），在文档中说明行为。方案 B 作为未来技术债。

# 4 Instant 效果的参数归属

纯 Instant 效果不创建 entity（优化路径内联 apply modifiers），因此：

- **不存在 EffectCallerParams 组件**（没有 entity 可以附加）。
- **CallerParams 仅在 Proposal 阶段生效**：merge 到 ConfigContext 后，在 OnPropose/OnCalculate 的 Graph 中可用。
- **Blackboard 写入**：写入到 Target entity 的 Blackboard（因为没有 effect entity 的 Blackboard）。
- **OnApply Phase Listeners 仍触发**：内联 apply 后，通过 `EffectPhaseExecutor.DispatchPhaseListeners` 显式分发。全局/实体 Phase Listener 的可观察性不因优化路径丢失。

如果 Instant 效果需要 CallerParams 在后续阶段使用（如 OnResolve 的 Graph），或需要 OnApply Pre/Post Graph 执行，则需要放弃纯 Instant 优化路径，创建 entity。这通过以下方式实现：
- 配置 `phaseGraphs`（`IsPureInstantTemplate` 检测到 `PhaseGraphBindings.StepCount > 0` 时自动走 entity 路径）
- 或将 `LifetimeKind` 设为 `After` + `DurationTicks=0`（立即过期的 After）
