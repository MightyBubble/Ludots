# fx-21 runtime spec · 进度完成

> 引擎实现任务书。第一性需求见 [fx-21 PRD](../prd/fx-21-progression.md)；现状见 [reference](../reference/fx-21-progression.md)。

## 1. 概述
进度完成合同：作用域宿主解析、等级变更应用、独占计划。

## 2. 设计
- HandleCompleteProgression：组装 RoleResolverContext(actor=Source, subject=Target, explicitScopeHost=TargetContext)，TryResolveScopeHost 按 self/explicit/命名三态解析宿主，ProgressionEvaluator.TryApply 按 id 与变更量应用。
- 变更编译保持三态：设级（level）/ 增量（delta）/ 完成（缺省）；level 与 delta 互斥由 loader 保证。
- 注册为 External(Progression)：效果计划独占。

## 3. 精确语义与不变量
- 一次效果执行至多一次进度变更；变更整体原子。
- 宿主必须已携带进度状态缓冲（ProgressionStateBuffer），缺失即抛错——效果不负责创建状态面。
- 等级只进不退：负向变更不可表达（level/delta 均正，缺省为完成）。

## 4. 迁移与治理
现状即基线。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-21 PRD](../prd/fx-21-progression.md) · [reference](../reference/fx-21-progression.md)
