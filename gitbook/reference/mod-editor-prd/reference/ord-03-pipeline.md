# ord-03 reference · 订单流水

> 现状参考。第一性需求见 [ord-03 PRD](../prd/ord-03-pipeline.md)；配置说明见 [ord-03 配置说明](../config/ord-03-pipeline.md)。

## 1. 现状快照

- 全局 OrderQueue（环形）：单笔 `SubmitAssigned` 走 GlobalIntake 预留→校验→入队→CommitAdmission；原子批量三形态 `TryEnqueueBatch`（逐条独立 id）/`TryEnqueueSharedBatch`（orderId 全 0 且 actor 唯一，首条生成 sharedOrderId，整批共享 AdmissionBatchId）/`TryEnqueueClusteredBatch`（CommandSource 非空按源聚类连续）；`TryDequeueBatch`/`TryPeekBatch` 整批出队校验连续。生产调用方：CompositeOrderPlanner、CoreInputMod LocalOrderSourceHelper、RoadMoveOrderExpander。
- 准入结果缓冲双代：current/pending ×（items+rejections）；`BeginLogicStep` 换代并前向携带未配对已接受项；`EndEntityIntake`/`EndLogicStep` 时序强制；容量拒绝写独立拒绝区，拒绝区也满 → `EnterTerminalFault`。主队列与 chainOrderQueue 共用。
- 实体 OrderBuffer：8 类型队列 + 1 pending（后写覆盖）+ 1 active；队列序（Priority 降，InsertStep 升）；空间 payload 带 RequiresOwner 所有权守卫——清理路径必须先 Release。
- 终态：`FinalizeActive` 校验（Completed 不带原因/Failed 必带）→ 清黑板+释放载荷 → 释放以该单为 trigger 的延续 → 发布终态 → 预备单提升。终态账本固定容量（见事实页）快照代际递增。
- 枚举：失败原因 17 值；提交结果 11 值及到失败原因的映射（接受态输入会抛异常，调用方须先排除）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 队列本体 / 单笔 | src/Core/Gameplay/GAS/Orders/OrderQueue.cs:29-579,73-123 |
| 三种批量 / 整批出队 | OrderQueue.cs:125-165,167-225,227-308,310-384 |
| 结果双代缓冲 | src/Core/Gameplay/GAS/Orders/OrderAdmissionResults.cs:103-724 |
| 换代/时序/拒绝区满 | OrderAdmissionResults.cs:562-639,671-705,345-381,513-518 |
| 结果枚举与映射 | OrderAdmissionResults.cs:13-26,50-66 |
| 实体缓冲 | src/Core/Gameplay/GAS/Components/OrderBuffer.cs:37-450 |
| 队列排序 / 所有权守卫 | OrderBuffer.cs:137-172,179-189 |
| 终态化 / 终态校验 | src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs:848-926,1145-1181 |
| 终态账本 | src/Core/Gameplay/GAS/Orders/OrderTerminalResults.cs:34-93 |
| 失败原因枚举 | src/Core/Gameplay/GAS/Components/OrderContinuationBuffer.cs:14-33 |
| 队列装配 | src/Core/Engine/GameEngine.cs:1008-1016 |

**相关文档**：[ord-03 PRD](../prd/ord-03-pipeline.md) · [ord-02 reference](ord-02-rules.md)
