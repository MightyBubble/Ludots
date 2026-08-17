# ai-11 reference · GOAP 与 HTN 规划

> 现状参考。第一性需求见 [ai-11 PRD](../prd/ai-11-goap-htn.md)；配置说明见 [ai-11 配置说明](../config/ai-11-goap-htn.md)。

## 1. 现状快照

- atoms：仅 id（GetOrAdd 首现注册）；他表引用未声明 atom 报错。
- projection：id/Atom/Op（IntEquals/IntGreaterOrEqual/IntLessOrEqual/EntityIsNonNull/EntityIsNull）/IntKey（语义串数字拒）+IntValue 或 EntityKey 互斥；键须 order_types 声明或内建；投影系统把 Order 黑板写入 256 位世界状态。
- utility：id/GoalPresetId 正/PlanningStrategyId（None/Goap/Htn/DirectTask）/Weight 默认 1/Bool[]{Atom,TrueScore 1,FalseScore 1}。
- goap_actions：id/Cost 1/Pre+Post{Mask[],Values[]}/Order 必填（OrderTypeKey/Id+SubmitMode+PlayerId+AbilityKey；OrderTagId 显式拒）/Bindings[]{Op:IntToOrderI0..I3/EntityToTarget/EntityToTargetContext,SourceKey}。
- goap_goals：id/GoalPresetId/HeuristicWeight 1/Goal{Mask,Values}。
- htn_domain（DeepObject）：Tasks[]{TaskId,FirstMethod,MethodCount}/Methods[]{MethodId,Cost,Condition,SubtaskOffset,SubtaskCount}/Subtasks[]{Index,Kind Compound|Action,RefId}/Roots[]{GoalPresetId,RootTaskId}。
- 三引擎：ActionLibraryCompiled256（SoA 位掩码+候选索引，IsApplicable/ApplyPost）；GoapAStarPlanner256（256 位加权 A*，节点池默认 4096）；HtnPlanner256（栈式 DFS+方法回退）；GOAP 按世界状态版本增量重规划；执行统一 PlanExecutor.TrySubmitOrder。
- 真实样本：ai_demo 旧栈五文件（各 1 条）+ htn_domain 空表。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| atoms/projection/utility/goap 编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:38-381 |
| 黑板键域校验 | AiConfigLoader.cs:1526-1558（RequireOrderBlackboardKey） |
| atom 注册表 | src/Core/Gameplay/AI/WorldState/AtomRegistry.cs |
| 世界状态位 | src/Core/Gameplay/AI/WorldState/WorldStateBits256.cs |
| 动作库 SoA | src/Core/Gameplay/AI/Planning/ActionLibraryCompiled256.cs、ActionCandidateIndex256.cs |
| GOAP A*（4096 池） | src/Core/Gameplay/AI/Planning/GoapAStarPlanner256.cs:20 |
| HTN 分解 | src/Core/Gameplay/AI/Planning/HtnPlanner256.cs、HtnDomainCompiled256.cs、HtnDomainTypes256.cs |
| 版本触发重规划 | src/Core/Gameplay/AI/Systems/GoapPlanningSystem.cs:50-72 |
| 统一执行出口 | src/Core/Gameplay/AI/Planning/PlanExecutor.cs:10 |
| 目标选择/计划执行系统 | src/Core/Gameplay/AI/Systems/AIGoalSelectionSystem.cs、AIPlanExecutionSystem.cs、HtnPlanningSystem.cs、WorldStateProjectionSystem.cs |
| 真实例 | mods/showcases/ai_demo/AIDemoMod/assets/AI/ |

**相关文档**：[ai-11 PRD](../prd/ai-11-goap-htn.md) · [ai-02 reference](ai-01-utility-overview.md)
