# ai-09 runtime spec · 行为树

> 引擎实现任务书。第一性需求见 [ai-09 PRD](../prd/ai-09-behavior-trees.md)；现状见 [reference](../reference/ai-09-behavior-trees.md)。

## 1. 概述

BT 加载与执行合同：扁平声明→BFS 打包、严格枚举、叶绑定语义、跨波续跑、四上限。

## 2. 设计

- PackTree 合同保持：BFS 遍历、id 去重、多父/不可达拒绝、action 仅 ScriptSlice+ActionLib Require。
- 执行合同保持：Condition 的 ScriptSlice 必须 halt（ReturnInt≠0=Success）；Action 可 Yield，cursor 与 GraphExecutionCursor 跨波保持；RestartAllThinking 重置。
- 上限保持：MaxNodesPerTree=64、MaxStackDepth=16、DefaultThinkPeriodTicks=12、DefaultScriptBudgetSteps=32（随 facts 再生）。
- **治理项（引 todo/ai.md）**：I2——BT/HFSM 枚举 ignoreCase:false 与 utility 十表 OrdinalIgnoreCase 并存，统一规则或双语文档；I10——schema 存在但不参与流水线校验，决定是否挂接（挂接即得编辑期结构校验，需评估额外启动成本）。

## 3. 精确语义与不变量

- 一节点至多一父；从 root BFS 可达节点数=nodes 总数（否则拒绝）。
- Condition 叶脚本若未 halt 报错；Action 叶 Yield 不消耗 Success/Failure 判定。
- 每 Tick 每树消耗脚本步 ≤ scriptBudgetSteps。

## 4. 迁移与治理

现状即基线；I2/I10 处置入 todo/ai.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-09 PRD](../prd/ai-09-behavior-trees.md) · [reference](../reference/ai-09-behavior-trees.md)
