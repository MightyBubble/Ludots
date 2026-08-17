# ai-11 reference · 行为树

> 现状参考。第一性需求见 [ai-10 PRD](../prd/ai-09-behavior-trees.md)；配置说明见 [ai-10 配置说明](../config/ai-09-behavior-trees.md)。

## 1. 现状快照

- 配置：主仓 `assets/AI/behavior_trees.json` 唯一树 bt.patrolChaseAttack（Selector/Sequence/Condition/Action + leaf ScriptSlice + action bt.xxx，共 9 节点）；schema 存在且唯一（kind 四值/leaf 五值枚举），但**不参与流水线校验**（I10）。
- 加载：root+非空 nodes、id 去重、枚举 ignoreCase:false（I2）、PackTree BFS 禁多父禁不可达、action 仅 ScriptSlice 合法、action→GraphActionCatalog.Require(name, BehaviorTree)。
- 执行：think wave 调用方驱动（RestartAllThinking/TickAll(scriptBudgetSteps)）；Condition 必须 halt（ReturnInt≠0=Success）；Action 可 Yield 跨波续跑（cursor 恢复）；叶另有 AlwaysSuccess/AlwaysFailure/HoldRunning 三绑定。
- 上限：MaxNodesPerTree=64、MaxStackDepth=16、DefaultThinkPeriodTicks=12（60Hz 下 0.2s）、DefaultScriptBudgetSteps=32。
- 真实驱动例：CapabilityStandardBehaviorTreeArenaMod 每 think wave Restart+TickAll 32。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 上限常量 | src/Core/Gameplay/AI/BehaviorTree/BehaviorTreeDefinition.cs:27-32 |
| 树解析与 PackTree | src/Core/Gameplay/AI/Config/GraphBehaviorDefinitionLoader.cs:64-191 |
| 枚举严格解析 | GraphBehaviorDefinitionLoader.cs:402-440 |
| 执行世界（栈/cursor/叶求值） | src/Core/Gameplay/AI/BehaviorTree/BehaviorTreeWorld.cs |
| 工厂与节点打包 | src/Core/Gameplay/AI/BehaviorTree/BehaviorTreeFactory.cs |
| 真实驱动例 | mods/showcases/capability_standard/CapabilityStandardBehaviorTreeArenaMod/Runtime/BehaviorTreeArenaRuntime.cs:129-131 |
| 资产与 schema | assets/AI/behavior_trees.json、assets/AI/behavior_trees.schema.json |

**相关文档**：[ai-10 PRD](../prd/ai-09-behavior-trees.md) · [ai-11 reference](ai-10-hfsm.md)
