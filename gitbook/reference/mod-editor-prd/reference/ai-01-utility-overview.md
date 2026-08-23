# ai-01 reference · AI 行为层总论

> 现状参考。第一性需求见 [ai-01 PRD](../prd/ai-01-utility-overview.md)；配置说明见 [ai-01 配置说明](../config/ai-01-utility-overview.md)。

## 1. 现状快照

- 代码布局：src/Core/Gameplay/AI/ 九子目录 67 文件——BehaviorTree 6 / Components 7 / Config 7 / Fsm 5 / Planning 19 / Systems 8 / Tasks 3 / Utility 8 / WorldState 6。
- 目录注册：AiConfigCatalog 18 条目，全部 ArrayById，唯 htn_domain DeepObject。
- 主仓 assets/AI 仅 4 文件（behavior_trees+schema、hfsm+schema）；效用十表由 mod 提供（utility_autocast 11 文件最全）。
- 加载序：atoms→projection→utility goals→goap_actions→goap_goals→htn_domain→CompileUtilityRuntime（十表）→GraphBehaviorDefinitionLoader（BT+HFSM）→AiCompiledRuntime 九字段。
- 三接缝现状：GraphScore 图经 RequireKind+UtilityAiGraphSafety 黑名单 18 个写 op（编译期+运行期双验）；Tasks 仅 SubmitOrder 落 OrderQueue；AbilityKey 经 AbilityIdRegistry+AbilityDefinitionRegistry 双查。
- 死代码：AiConfigModels.cs 9 个 POCO 无消费方（I9）；utility 十表无 schema（I10）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 18 表目录 | src/Core/Gameplay/AI/Config/AiConfigCatalog.cs:10-27 |
| 编译入口与加载序 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:38-386 |
| 效用十表聚合 | AiConfigLoader.cs:389-485 |
| BT+HFSM 加载 | src/Core/Gameplay/AI/Config/GraphBehaviorDefinitionLoader.cs |
| 编译产物 | src/Core/Gameplay/AI/Config/AiCompiledRuntime.cs |
| 图写 op 黑名单 | src/Core/Gameplay/AI/Utility/UtilityAiGraphSafety.cs:26-43 |
| 效用运行环 | src/Core/Gameplay/AI/Systems/UtilityAiSystems.cs:13-246 |
| 死 POCO | src/Core/Gameplay/AI/Config/AiConfigModels.cs |
| 主仓行为资产 | assets/AI/behavior_trees.json、assets/AI/hfsm.json |
| mod 效用全集 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/ |

**相关文档**：[ai-01 PRD](../prd/ai-01-utility-overview.md) · [ai-02 reference](ai-02-inputs.md)
