# ai-07 reference · 决策者与档案

> 现状参考。第一性需求见 [ai-06 PRD](../prd/ai-05-dm-profiles.md)；配置说明见 [ai-06 配置说明](../config/ai-05-dm-profiles.md)。

## 1. 现状快照

- decision_makers：Decisions 必填且连续区间；SelectionMode 默认 UtilityScore（二值 UtilityScore/FixedPriority）；SwitchMargin 默认 0 仅 UtilityScore 生效。
- profiles：DecisionMakers 必填连续；DecisionIntervalSteps 默认 1 正数；MaxCandidates 默认 64 正数；DefaultStance 语义键、DefaultStanceId 数字显式拒绝；十表非空时 profiles 必须至少一条。
- 择优：超 best+margin 才换；margin 内先比 PriorityBucket 再比 DistanceSq；订单缓冲占用时跳过思考。
- DefaultStance 编译后无运行消费（I6，仅测试出现）。
- 真实资产：utility_autocast 各 1 条（DM.Mage 三决策；Profile.Mage interval 1 / MaxCandidates 32）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 决策者编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:945-997 |
| 档案编译 | AiConfigLoader.cs:999-1071 |
| profiles 非空强制 | AiConfigLoader.cs:466-469 |
| 择优与换挡比较 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:899-951 |
| 节奏与缓冲跳过 | src/Core/Gameplay/AI/Systems/UtilityAiSystems.cs:137-221 |
| scratch 容量 | UtilityAiSystems.cs:225-245 |
| 组件 | src/Core/Gameplay/AI/Components/UtilityAiRuntimeComponents.cs:5-24 |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decision_makers.json、profiles.json |

**相关文档**：[ai-06 PRD](../prd/ai-05-dm-profiles.md) · [ai-07 reference](ai-06-target-filters.md)
