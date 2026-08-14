## GAS Composition Gate — Self Review

- **Task / Issue**: S9 · L2 宿主走 L1 正式执行前门（引入执行帧）
- **Date**: 2026-08-14
- **Agent / Author**: Cursor Grok 4.6

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增 graph 节点、profile enum 或平行执行器；只把已有 `GraphExecutor` 收成唯一校验前门，用执行帧消灭部分初始化的 `GraphExecutionState`，并拆掉「掉出尾部算成功」与「预算挂起当未开始」两条兼容性 fallback。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 执行帧构造与一次性校验 | 0 | `GraphFrame` + `GraphExecutor` |
| 七个宿主改走前门 | 2 | 已有 BT / HFSM / Level / Effect / Performer / Query / Aim 宿主 |
| 装载期程序校验 | 0 | `GraphKindOperationPolicy.ValidateProgram`（叠在已有 `GraphYieldPurityValidator` 上） |
| 显式终结 + 跳转目标 | 0 | 登记期校验 + VM `pc` 越界失败关闭 |
| 预算挂起续跑 | 0 | `GraphExecutionStatus.BudgetSuspended` + BT 续跑判断 |

### 3. Reuse list

- Handlers: 已有 `GasGraphOpHandlerTable`；`Execute` / `ExecuteSlice` 收成 internal
- Queues / Systems: 无新 System
- Resolvers / Registries: `GraphProgramRegistry`、`GraphKindOperationPolicy`、`GraphYieldPurityValidator`、`GraphExecutor`
- Existing presets / graphs: 不改资产 schema；线性/Query 编译器在无 `next` 的末端发出已有 `HaltReturnInt`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

`Register` / `ReplaceProgram` 校验失败必须回滚到登记前状态（与 S1 环检测同一条回滚路径）。执行帧构造失败不得留下半初始化状态。

### 6. Config SSOT

行为配置落在: 已有 graph / ActionLib / FuncLib；本票是执行门与帧合同，不是新 JSON schema。

是否新增 JSON schema: NO — 不通过组合表达，因为这是执行基础设施收口，不是玩法变体。

文档更新路径: 本票不把合同改成「已落地」；不新增 AAC 平行 ADR。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未拓宽 Script 方言
- [x] 未放宽 kind 校验
- [x] 未新建平行 Registry / 执行器
- [x] 未删除八个家族 Mod

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

---

## 任务摘要

L1 正式前门 `GraphExecutor` 在生产路径零调用者；七个宿主直连 `GasGraphOpHandlerTable`，跳过 kind / 能力 / 寄存器尺寸检查，且手工拼装不完整的 `GraphExecutionState`（`Programs` 为空导致生产路径写得出 `InvokeScript` 却执行不了）。BT 只认 `Yielded`，预算耗尽会清空寄存器并从 pc=0 重跑。掉出程序尾部被当成成功。本票收口执行门，不是新 op / 新 enum。

## 判断标准结论

通过。交付物是已有执行入口上的帧与校验闸门，不是新 enum 或新管线。

## 复用 / 新增表

| 类型 | 项 |
|------|-----|
| 复用 | `GraphExecutor`、`GraphKindOperationPolicy`、`GraphProgramRegistry`、`GraphYieldPurityValidator`、`GraphExecutionCursor` / `GraphSliceResult`、`GraphVmLimits`、S1 环检测与 `ContainsYield` |
| 新增 Layer 0 op | 无 |
| 新增类型 | `GraphFrame`、`GraphEntityPreset`（E[2] 三种预置含义） |
| 新增状态 | `GraphExecutionStatus.NotStarted`、`BudgetSuspended` |
| 新增错误码 | `GAS.GRAPH.ERR.KindMismatch`、`GAS.GRAPH.ERR.JumpOutOfRange`、`GAS.GRAPH.ERR.MissingHalt`、`GAS.GRAPH.ERR.PcOutOfRange`、`GAS.GRAPH.ERR.RegisterOutOfRange` |
| 禁止 | 新 profile DSL、平行执行器、放宽 kind、拓宽 Script 方言 |
