# Case E 全链下单（图桥）— 切1 验收报告

- 日期：2026-09-14
- 分支：`graph-input-order-chain`
- 合同正本：`mods/showcases/case_e_selection/CaseESelectionMod/docs/input-config-constitution.html` §12

## 结论

输入→下令链路的首条全图链打通并锁定：**拖框选中 → 右键 → 边沿图 → SubmitCommandIntent op → 意图提交缓冲 → 下令域 drain（LIFO 声明解析）→ 授权 → moveTo 订单落到活跃集成员**。全程零 mod 业务代码、零引擎特权键。

## 链路（谁在什么时机做什么）

1. `CaseE.Command`（`<Mouse>/rightButton`，press 边沿）由 battle context 的 bindings 门控。
2. `graph.case_e.command_commit`（battle 挂载的触发图）：LoadPointerScreenX/Y → ScreenPointToGround → JumpIfFalse（地面未解析即不提交）→ `SubmitCommandIntent`（condition=target 端口可选，本次仅地面事实）。
3. op 把 (rep=caster, 地物点) 写入 `CommandIntentSubmissionBuffer`（容量=commandIntentScratchCapacity，超限具名抛错）——图执行期内不路由。
4. 下一 tick `CommandIntentBufferDrainSystem`（LocalInput 相位）drain：rep 活跃 context 链 LIFO 取最近声明 `activeCollectionKey` 的 context（battle → `selected`，载体=指挥官）；读载体集合得下令对象；`intent.command.default` 路由（RouteGroup）→ `dispatch.all_together` 扇出 → 授权（PlayerOwner→Owns 边）→ moveTo（WorldPositionCm=右键地面点）经共享批入 OrderQueue → 陆战队 OrderBuffer。
5. 未选中单位无单（无隐式兜底：链上无声明 context ⇒ 具名拒绝）。

## 证据

- `CaseESelectionShowcaseAcceptanceTests.CommandIntentFullChain_RightClickOrdersBoxSelectedUnitsThroughGraphBridge`（全链 headless：选中 2 单位、右键 (600,300)、断言 marine1/2 订单类型/玩家/执行者/目标地面点、marine3/4 无单、选中集不被下单改动）——**通过**。
- `CaseESelectionShowcaseAcceptanceTests.BoxSelectFullChain_...`（既有框选链回归）——**通过**。
- `GraphOpsNodeGalleryContextAcceptanceTests.SubmitCommandIntentVignette_PushesIntoTheSubmissionBuffer`（op 级画廊：图执行后缓冲恰 +1、路由留在下令域相位）——**通过**。
- GAS 组合门自审：`artifacts/gas-composition-gate.md`（本次任务段）。
- 画廊覆盖登记：`assets/GAS/graph_node_op_coverage.registry.json` 新增 SubmitCommandIntent（covered，含画廊 vignette/入口 mod/launcher 绑定）。

## 边界与已知事实

- 引擎侧新增：GraphNodeOp 483、CommandIntentSubmissionBuffer、CommandIntentBufferDrainSystem、GameEngine 装配（LocalInput，早于 AxisMoveOrderSystem）。
- 群组布局（groupMoveTargetLayout）与 ICommandActorExpander 尚未进图桥（切2/切3 随 formation 迁移补）；本次 moveTo 扇出为并行共享批。
- 本工作树存在**先于本分支**的未提交 WIP（narrative/launcher/game.json 等）与既有测试失败（GasTests 145 / ArchitectureTests 5，含 3 个前人新 op 未登记画廊）；与本切片无关，未处理。
