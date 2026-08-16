# ai-06 · 任务

> 第一性需求 · 已冻结。配置写法见 [配置说明](../config/ai-07-tasks.md)；编辑器需求见 [UXD](../uxd/ai-07-tasks.md)；引擎实现见 [runtime spec](../spec-runtime/ai-07-tasks.md)；编辑器实现见 [editor spec](../spec-editor/ai-07-tasks.md)；现状见 [reference](../reference/ai-07-tasks.md)。

## 1. 定位

task 是决策胜出后的动作槽：四种 Kind 中只有 SubmitOrder 真正做事——构造一条 Order 压进 OrderQueue。Sequence/Parallel/ParallelComplete 是保留的组合名位，现状行为近乎等价（I5）。

## 2. 产品承诺

- **订单即边界**：效用 AI 对世界的唯一写动作是提交订单；订单参数（类型/技能/槽位/玩家/整参/空间目标）全部在任务上声明。
- **引用双验**：OrderTypeKey/OrderId 二选一且互验；AbilityKey/AbilityId 同理。
- **槽位回退链**：task 槽位 → decision 槽位 → 按技能反查槽位；构造 Order 时槽位写 I0、IntArg1 写 I1、目标位置写 Spatial。
- **失败即 Blocked**：TryEnqueue 失败本轮不提交，状态 Blocked 可 trace。

## 3. 运行行为

决策胜出后逐任务执行：SubmitOrder 组装 Order（Actor/Target/OrderTypeId/PlayerId/SubmitMode）并按回退链填槽位，TryEnqueue 成功即 Complete；Sequence 现状是跳过、Parallel/ParallelComplete 只置"曾有要求"标记——三种组合 Kind 无实际编排（I5，命名误导）。

## 4. 异常承诺

未知 Kind、SubmitOrder 缺 OrderType 引用或双写不一致、SubmitMode 越界、AbilityKey 未注册——启动失败并带路径。

**相关文档**：[配置说明](../config/ai-07-tasks.md) · [ai-03](ai-04-decisions.md) · [cfg-04](cfg-04-config-tables.md)
