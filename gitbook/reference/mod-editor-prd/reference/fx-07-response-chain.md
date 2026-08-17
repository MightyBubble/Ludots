# fx-10 reference · 响应链

> 现状参考。第一性需求见 [fx-09 PRD](../prd/fx-07-response-chain.md)；配置说明见 [fx-09 配置说明](../config/fx-07-response-chain.md)。

## 1. 现状快照

- ResponseChainListener：容量 8；四类型 Hook=0/Modify=1/Chain=2/PromptInput=3；EventTagIds 0=通配；Priorities 大者优先；ModifyValues+ModifyOps；ResponseGraphIds>0 为动态图约定槽位（E[0]/E[1]/F[0]/I[0]）——图路径无消费点，Collect 只用静态值（todo/effect.md E5）。
- 窗口状态机 None→Collect→WaitInput→Resolve：开窗=根提案+OnPropose 通过+声明参与；响应入队容量见事实页。
- Collect：Hook 置 Cancelled；Modify 改窗口修正；Chain 新提案（数量/深度上限见事实页）；PromptInput 置交互；步数上限见事实页，熔断清队。
- WaitInput：Prompt 与 OrderRequest 双容量原子发布；ChainPass 连续 2 次关窗；ChainNegate 累加；ChainActivateEffect 动态。
- Resolve：从尾向前；非根项否决（i>0 且有剩余 negate）→OnCalculate→内联或实体化。
- RootBudgetTable：开放寻址+stamp O(1) 清空；TryConsume(rootId, limit) 中 rootId==0 恒放行；超限抛 GAS.FAN_OUT.ERR.RootBudgetExceeded（上限见事实页）；事务 checkpoint/Commit/Rollback。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 监听结构与四类型 | src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs:8-14, 45-124 |
| 开窗条件 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:419-508 |
| 响应入队容量 | EffectProposalProcessingSystem.cs:1333-1366 |
| Collect 四动作与熔断 | EffectProposalProcessingSystem.cs:511-603, 515-520 |
| 静态值消费（图路径未接线） | EffectProposalProcessingSystem.cs:526-576 |
| 等输入原子发布 | EffectProposalProcessingSystem.cs:624-685 |
| 关窗与动态激活 | EffectProposalProcessingSystem.cs:762-767 |
| 从尾向前裁决与非根否决 | EffectProposalProcessingSystem.cs:988-1154, 1017-1037 |
| 根预算表结构 | src/Core/Gameplay/GAS/RootBudgetTable.cs:105-118, 128, 55-92 |
| 预算超限报错 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:294-298 |

**相关文档**：[fx-09 PRD](../prd/fx-07-response-chain.md) · [fx-10 reference](fx-08-phase-listeners.md)
