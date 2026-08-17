# ai-01 runtime spec · AI 行为层总论

> 引擎实现任务书。第一性需求见 [ai-01 PRD](../prd/ai-01-utility-overview.md)；现状见 [reference](../reference/ai-01-utility-overview.md)。

## 1. 概述

AI 配置编译合同：18 表固定加载序、单一 AiCompiledRuntime 产物、三接缝的读写边界。

## 2. 设计

- 加载序与产物九字段保持（Atoms/ProjectionTable/GoalSelector/ActionLibrary/GoapGoals/HtnDomain/HtnRoots/UtilityRuntime/Behavior）；效用十表聚合与空态 Empty 保持。
- 三接缝保持：GraphScore 编译期 RequireKind=Score+写 op 黑名单，运行期再验；SubmitOrder 只落 OrderQueue；AbilityKey/AbilityId 双重校验互验。
- **治理项（引 todo/ai.md）**：I9——AiConfigModels.cs 全部 9 个 POCO 死代码（loader 全程 JsonObject 手工解析），删除或改为真正的反序列化目标；I10——utility 十表无 schema（仅 BT/HFSM 有且不参与流水线校验）。
- 单一编译入口 LoadAndCompile 不变；新增表必须同时挂 AiConfigCatalog 与本方法，避免死目录条目（对齐 T3）。

## 3. 精确语义与不变量

- 效用十表全部为空 ⇒ UtilityRuntime=Empty 且系统早退（IsEnabled=false）。
- 十表任一非空 ⇒ profiles 必须非空，且校验上下文必须在场。
- 加载序是合同：atoms 先于 projection，utility goals 先于 GOAP/HTN，效用十表先于 BT/HFSM。

## 4. 迁移与治理

现状即基线；I9/I10 处置入 todo/ai.md。十八表增表走 cfg-04 新表审批流。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-01 PRD](../prd/ai-01-utility-overview.md) · [reference](../reference/ai-01-utility-overview.md)
