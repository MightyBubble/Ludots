# fx-09 reference · 提案窗口与 Instant 内联

> 现状参考。第一性需求见 [fx-08 PRD](../prd/fx-06-proposal-window.md)；配置说明见 [fx-08 配置说明](../config/fx-06-proposal-window.md)。

## 1. 现状快照

- 纯相位=OnPropose/OnCalculate；OnPropose 要求 Validation 图、其余相位 Effect（executor 按 kind 把关）。
- 编译期 fail-closed（EffectExecutionPlan）：纯相位非纯操作计数>0 抛；监听图禁 InvokeBuiltin 与 LoadConfig*；纯相位监听图只许 Pure、非纯只许 Pure/GasTransactional。
- 运行期：OnPropose 走带验证结果的执行路径；结果寄存器 B[0] 执行前播种 0、图内须显式写 1；拒绝粘滞；空相位直接通过。
- 四窗口：Activation(OnResolve+OnHit+OnApply) / Period / Expire / Remove，全须 finalized；外部原子独占律在 CompileWindow 检查——仅 Activation 允许 External，须 Instant+恰 1 个+0 事务图+最后操作+不与 modifiers/grantedTags/listenerSetup 组合，违反抛 InvalidComposition；运行期监听器预检冲突抛。
- 实际 External 域仅 3 个：Displacement/Progression/Order。Unsupported（fail-closed）：Vision（RevealArea/Decay）、Exchange、Relationship（RemoveParent/EnsureLink）、全部 lifecycle 内建、图 op BeginLifecycleTransaction 与关系修改类。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 纯相位定义与图 kind 要求 | src/Core/Gameplay/GAS/Components/EffectPhaseListenerBuffer.cs:59-63 |
| executor kind 把关 | src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs:801-802 |
| 纯相位非纯计数检查 | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:364-388 |
| 监听图禁用 op | EffectExecutionPlan.cs:341-350 |
| 四窗口组织 | EffectExecutionPlan.cs:197-206, 125-130 |
| 独占律 CompileWindow | EffectExecutionPlan.cs:390-454 |
| 空相位放行 | EffectExecutionPlan.cs:381 |
| 验证执行与播种 | EffectPhaseExecutor.cs:899-911, 832-836, 883-888 |
| 运行期独占预检 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1421-1428 |
| External 域三枚 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:64-81 |
| 图侧 Unsupported op | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs:176-184 |

**相关文档**：[fx-08 PRD](../prd/fx-06-proposal-window.md) · [fx-09 reference](fx-07-response-chain.md)
