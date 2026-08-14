## GAS Composition Gate — Self Review

- **Task / Issue**: S1 · 图调用无界递归会杀进程（PR #942 修复计划 A1）
- **Date**: 2026-08-14
- **Agent / Author**: Cursor Grok 4.6

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 不新增 graph 节点、profile enum 或平行管线；只把已有 `InvokeScript` 边的环从「当没找到 Yield」改成装载期错误，并给已有执行游标补上调用深度与整棵调用树共享的步数预算。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 装载期拒环 | 0 | `GraphYieldPurityValidator` + `GraphProgramRegistry.Register` / `ReplaceProgram` |
| 热改过闸 | 2 | `LiveGasEditPipeline.Classify`（候选体）+ `ReplaceProgram`（硬闸） |
| FuncLib 纯闭包 | 2 | 已有 `GraphFunctionCatalogLoader`（环从 clean 改 error） |
| 运行期深度 / 共享步数 | 0 | `GraphVmLimits` + `GraphExecutionState` / `GraphExecutionCursor` + `GasGraphOpHandlerTable` |
| Yield 进 Invoke 目标 | 0 | 登记期 `ContainsYield` 标记；运行期 O(1) 读标记 |

### 3. Reuse list

- Handlers: 已有 `HandleInvokeScript`；不新增 opcode
- Queues / Systems: 无
- Resolvers / Registries: `GraphProgramRegistry`、`GraphIdRegistry`、`GraphFunctionCatalog`（热改 / FuncLib 解析）
- Existing presets / graphs: 不改资产；装载期只拒绝成环的 `InvokeScript` 图

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

热改 `ReplaceProgram` 失败必须回滚到替换前的程序体（已有 `CommitNextCastSafeFrame` rollback 列表）。装载期 `Register` 失败不得留下半登记的 id。

### 6. Config SSOT

行为配置落在: 已有 graph / FuncLib / ActionLib catalog；闸门是登记与执行合同，不是新 JSON schema。

是否新增 JSON schema: NO

文档更新路径: `gitbook/architecture/graph-layering-flow-and-behavior.md`（步数硬顶旁补上调用环与 invoke 深度）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未用「Script 不许 InvokeScript」回避问题
- [x] 未只加运行期深度而不做装载期拒环
- [x] 未尝试捕获 `StackOverflowException`

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

---

## 任务摘要

一张图 `InvokeScript` 指向自己或成环时，当前会在运行期递归到栈溢出并杀掉进程。三道旧闸门都不覆盖跨图调用。本票只修这一条。

## 判断标准结论

通过。交付物是已有 op 边上的装载/运行闸门，不是新 enum 或新管线。

## 复用 / 新增表

| 类型 | 项 |
|------|-----|
| 复用 | `GraphYieldPurityValidator.activeGraphs`、`GraphProgramRegistry`、`LiveGasEditPipeline`、`GraphFunctionCatalogLoader`、`GraphExecutionCursor`、`GraphVmLimits.MaxInstructionsPerExecution` |
| 新增 Layer 0 op | 无 |
| 新增常量 | `GraphVmLimits.MaxInvokeDepth`（与同文件 `MaxCallStackDepth` 并列） |
| 新增错误码 | `GAS.GRAPH.ERR.InvokeCycle`、`GAS.GRAPH.ERR.InvokeDepthExceeded`（沿用 `GAS.GRAPH.ERR.*` 诊断风格） |
| 禁止 | 新 profile DSL、平行加载器、重写 VM 核心 |

## 挂载点选择

选 `GraphProgramRegistry.Register` / `ReplaceProgram`，不是只挂 `GraphProgramConfigLoader.PatchAndRegister`。

理由：所有 `InvokeScript` 来源（配置装载、测试直登、画廊 Mod 直登、热改 `ReplaceProgram`）都经过这两处。`GraphYieldPurityValidator` 今天只挂在 FuncLib 装载与热改纯闭包检查上，挡不住直登脚本图。`GraphRuntime` 的架构守卫只禁止引用 `Gameplay.GAS`，同一程序集内复用 Host 校验器不破那道墙。

增量登记时尚未登记的调用目标视为「还没到」，不报错，避免 `PatchAndRegister` 的前向引用被误杀；环在第二个参与者登记时闭合并失败。热改路径：`Classify` 用候选程序体预检，`ReplaceProgram` 再硬拒。
