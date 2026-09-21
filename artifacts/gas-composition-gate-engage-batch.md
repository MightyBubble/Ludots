# GAS Composition Gate — SubmitEngageBatch（EQS 围城批量落位）

- **Task / Issue**: #1398 Case 3 补遗（攻击建筑→占城位点/围城位）+ #1600 批量节点批首件
- **Date**: 2026-09-21
- **Agent / Author**: Codex

## 1. Core judgment

新变体主要交付物: **A——新增原子 graph op（SubmitEngageBatch）+ 订单内核批量扇出**

结论: **PASS**

一句话理由: 围城落位 = EQS 选点 × 逐成员分配 × moveTo+续接施法 的批量迭代，图内零 for 循环（#1600 既定裁决），必须在单一 op 内完成；无任何 profile enum / preset 开关。

## 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| SubmitEngageBatch op（意图提交） | 0 | GasGraphOpHandlerTable + 意图缓冲 engage 通道 |
| EQS 选点 + 逐成员分配 + 续接规划 | 0 | CommandIntentBufferDrainSystem（订单内核相位，复用 CompositeOrderPlanner） |
| 围城档案（环半径/测试权重） | 2 | Spatial/eqs_queries.json（纯数据，#857 配置面） |
| 到位后开火 | 0（复用） | OrderContinuationSystem 既有 move-then-cast 机制，零新码 |
| 占位 | 0 | EngageSlotClaims SoA 组件（int 三元组存成员，无 Entity 引用） |

## 3. Reuse list

- Handlers: CompositeOrderPlanner（SubmitWithMoveAnchor 重载，事务/回滚原样）、OrderContinuationStateInstaller、InputOrderActorAuthorization
- Queues / Systems: CommandIntentSubmissionBuffer（新增 engage 通道）、CommandIntentBufferDrainSystem、OrderContinuationSystem、MoveToWorldCmOrderSystem、AbilityExecSystem
- Resolvers / Registries: EqsQueryRegistry（新，薄壳）、StringIntRegistry、EqsInfluenceConfigLoader（ParseQueryEntry/CreateQuery 公开复用）、ConfigKeyRegistry、GraphProgramSymbolPatcher
- Existing presets / graphs: Ring 生成器、Distance 测试、Best 选择、graph.ballista.route 双测链三分支（建筑分支改接）

## 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| SubmitEngageBatch(486) | 一次围城意图进 §12 缓冲（rep/slot/target/profile/orderTypeKey） | 逐成员 EQS 选点+批量落位是 op 内迭代；SubmitCast 是单意图、SubmitCommandIntent 是单落点，组合表达需要图内循环（合同禁止） |

## 5. Transaction boundary

SubmitWithMoveAnchor：续接注册失败/主订单提交抛错时回滚释放续接注册与空间载荷（沿用 planner 既有回滚）。占位写入在订单提交成功之后，成员死亡由懒修剪回收。

## 6. Config SSOT

行为配置落在: `Spatial/eqs_queries.json`（围城档案：Ring 半径/数量 + Distance/Influence 权重 + Best）；路由在 `graph.ballista.route`（双测链三分支）；射程在 `abilities.json targeting.castRangeCm`。

是否新增 JSON schema: **NO**——全部复用 #857 的 EQS 配置面、abilities targeting、graph 节点字段（新增 engageProfile/orderTypeKey 两个符号字段，随 op 合同走）。

## 0-GC 备注（用户点名要求）

意图缓冲/占位/选点全部预分配定容（EngageIntentSubmission 数组、EqsItem[256] 池、bool[256] 使用位图、EngageSlotClaims fixed 数组）；drain 环内零分配零 LINQ；EQS Run 本身 0-alloc（span 缓冲契约）。冷路径=每次点击一次，热路径（每帧）零新增成本。
