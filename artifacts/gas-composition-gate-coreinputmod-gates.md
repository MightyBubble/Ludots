# GAS Composition Gate — 删除时间轴输入门

本票自审（CoreInputMod 退役 · 片 2）。不覆盖 `artifacts/gas-composition-gate.md` 正本。

## 任务摘要

删除技能时间轴里“半路停下来等玩家”的两种步骤 `InputGate`、`TargetCollectionGate`，以及只为它们服务的部件：能力输入请求队列 `AbilityInputRequestQueue`、应答缓冲 `InputResponseBuffer`、应答结构 `InputResponse`、应答系统 `GasInputResponseSystem`，和进度需求“等门回填目标上下文后再判”的延迟分支。

全仓 JSON 没有一处用到这两种步骤，唯一的生产者是 `GasInputResponseSystem`（读确认键回填目标）。玩家选目标、确认这件事改由交互上下文的图在下令前完成，目标随施法命令进入时间轴。

响应链的 PromptInput 仍用 `InputRequest` / `InputRequestQueue`，不在本次删除范围。

## GAS Composition Gate — Self Review

- **Task / Issue**: CoreInputMod 退役设计 §3.1（片 2）
- **Date**: 2026-09-28
- **Agent / Author**: Cursor Cloud Agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **不适用（纯删除，不新增变体）**

结论: **PASS**

一句话理由: 只删平行的“时间轴内等输入”通道，不新增 enum、preset 或管线；等输入这类需求回到已有的交互上下文图 + 施法命令。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 时间轴事件等待 | 0 | 既有 `EventGate`（保留） |
| 玩家选目标 / 确认 | 2 | 交互上下文 profile 的 triggers 图（`QueryFromCollection` → `SubmitCast`） |
| 进度需求判定 | 0 | 既有 `EvaluateProgressionRequirement`，起播时一次判定 |

### 3. Reuse list

- Handlers: 无新增
- Queues / Systems: `EffectRequestQueue`、`OrderQueue`（不动）；响应链 `InputRequestQueue` 保留
- Resolvers / Registries: `ProgressionRequirementEvaluator`（不动）
- Existing presets / graphs: 交互上下文提交图（不动）

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。进度需求不满足时在起播阶段取消，时间轴还没产生任何副作用。

### 6. Config SSOT

行为配置落在: 能力表 `GAS/abilities.json` 的 `exec.items`；玩家输入落在交互上下文 profile 的 triggers 图。

是否新增 JSON schema: NO。加载器对 `InputGate` / `TargetCollectionGate` 按未知类型报错，报出能力 id 和步骤序号。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（“先选目标再放技能”写在交互上下文图里，下令时带上目标）
