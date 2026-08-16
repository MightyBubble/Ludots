# ai-10 runtime spec · GOAP 与 HTN 规划

> 引擎实现任务书。第一性需求见 [ai-10 PRD](../prd/ai-11-goap-htn.md)；现状见 [reference](../reference/ai-11-goap-htn.md)。

## 1. 概述

世界状态族六表与三规划引擎合同：256 位世界观、投影、目标计分、A* 与 HTN 分解、统一执行出口。

## 2. 设计

- 加载序与互斥校验保持（atoms 先行；IntKey/EntityKey 按 Op 互斥；OrderTagId 显式拒）。
- 三引擎合同保持：ActionLibraryCompiled256（SoA 位掩码+候选索引，IsApplicable/ApplyPost）；GoapAStarPlanner256（256 位世界状态加权 A*，4096 节点池，默认容量可注入）；HtnPlanner256（栈式 DFS 分解+方法回退）。
- 重规划策略保持：世界状态 Version 未变不重规划；执行统一 PlanExecutor.TrySubmitOrder。
- **治理项（引 todo/ai.md）**：I10——六表无 schema；htn_domain 为唯一 DeepObject 表，编辑器需专门结构视图（cfg-04 新表审批同规）。

## 3. 精确语义与不变量

- atom 槽位在首次 GetOrAdd 时分配；引用未声明 atom ⇒ 编译失败。
- 投影键域 = order_types 声明键 ∪ 内建键；语义串数字拒。
- GOAP 与 HTN 共享 ActionLibrary 与世界状态位义；DirectTask 直发不经规划。
- 计划执行每步 TrySubmitOrder，失败保留计划待下步重试。

## 4. 迁移与治理

现状即基线；ai_demo 旧栈（五文件 + 空 htn_domain）是活体样本，重构时保兼容。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-10 PRD](../prd/ai-11-goap-htn.md) · [reference](../reference/ai-11-goap-htn.md)
