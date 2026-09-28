# fx-07 reference · 响应链

> 现状参考。第一性需求见 [fx-07 PRD](../prd/fx-07-response-chain.md)；配置说明见 [fx-07 配置说明](../config/fx-07-response-chain.md)。

## 1. 现状快照

- ResponseChainListener：容量 8；四类型 Hook=0/Modify=1/Chain=2/PromptInput=3；EventTagIds 0=通配；Priorities 大者优先；ModifyValues+ModifyOps；ResponseGraphIds>0 为动态图约定槽位（E[0]/E[1]/F[0]/I[0]）——图路径无消费点，Collect 只用静态值（todo/effect.md E5）。
- 窗口状态机 None→Collect→WaitInput→Resolve：开窗=根提案+OnPropose 通过+声明参与；响应入队容量见事实页。
- Collect：Hook 置 Cancelled；Modify 改窗口修正；Chain 新提案（数量/深度上限见事实页）；PromptInput 置交互；步数上限见事实页，熔断清队。
- WaitInput：先查 OrderRequest 容量再开等待状态（`ResponseChainPromptState`），不会出现 HUD 看不到的孤儿等待。问的是第一个 PromptInput 监听的主人（监听实体上的 `PlayerOwner`，没有就报 `PromptResponderWithoutOwnerError`），每次提问只收被问者的第一次回答，别人的单记 `RejectedValidation`，答过再发由节点拒绝并计数。
  - ChainPass：一次关窗，按后进先出结算。
  - ChainNegate：关窗并作废链上最近一环，打开窗口的根效果也能被作废。
  - ChainActivateEffect：把监听给的效果接到链上，出手的是回答者，目标是对面（回答者是当前链结的出手人时打它的目标，否则打它的出手人）；接完关掉这次提问并重新收集，可能反过来问对面。
  - 被问的人已被销毁（比如所在地图卸载）时关窗并计入 `AbandonedForMissingActorCount`。
- 模板声明：实体模板可直接写 `ResponseChainListener` 组件，`{ "responses": [{ "category", "type": "Hook|Modify|Chain|PromptInput", "priority", "effect"（Chain / PromptInput）, "modifyValue" + "modifyOp"（Modify） }] }`，1 到 8 条，未知字段或名字加载时报错。组件加上或拿掉时监听缓存自动刷新。
- 回答入口：mod 在交互情境里接 `ResponseChain.PromptOpened` / `ResponseChain.PromptClosed` 地图事件（来源是窗口的出手人，目标是被问玩家的代表实体），用 `SubmitResponseChainOrder` 节点提交 chainPass / chainNegate / chainActivateEffect；引擎不装按键消费者，不带默认键位，也不画提示界面，提示面板由 mod 自己声明。完整示例见 `mods/showcases/tcg_demo/TcgPromptShowcaseMod`（验收 `TcgPromptShowcaseAcceptanceTests`）。
- Resolve：从尾向前；有剩余 negate 的链结被否决（含根）→OnCalculate→内联或实体化。
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

**相关文档**：[fx-07 PRD](../prd/fx-07-response-chain.md) · [fx-08 reference](fx-08-phase-listeners.md)
