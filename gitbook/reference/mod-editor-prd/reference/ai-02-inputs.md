# ai-02 reference · 效用输入

> 现状参考。第一性需求见 [ai-02 PRD](../prd/ai-02-inputs.md)；配置说明见 [ai-02 配置说明](../config/ai-02-inputs.md)。

## 1. 现状快照

- 8 种 Kind 全集：Constant/DistanceToTarget/TargetPriorityBucket/ActuatorReadiness01/GraphScore/TargetHasTag/SourceHasTag/AbilityReady。
- 默认值：Constant.Value=1（整数，I1）、TargetPriorityBucket.DefaultPriority=0；ActuatorId 必填正数；GraphScore 要求 GraphKey/GraphId 二选一且 kind=Score；AbilityReady 要求 AbilityKey/AbilityId 必填。
- Kind 解析 OrdinalIgnoreCase；BT/HFSM 枚举解析 ignoreCase:false——同目录两套大小写规则（I2）。
- 采样越界 id 返 0；AbilityReady 内部走 IsAbilityReady（sharedCooldownTagId=0）。
- 真实资产仅 utility_autocast 2 条（Distance + GraphScore）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 8 Kind 编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:587-648 |
| 运行采样 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:547-576 |
| Score 图安全校验 | src/Core/Gameplay/AI/Utility/UtilityAiGraphSafety.cs |
| 图引用解析 | AiConfigLoader.cs（ResolveGraphReference） |
| 技能引用双查 | AiConfigLoader.cs:1119-1140 |
| 组件来源 | src/Core/Gameplay/AI/Components/UtilityAiRuntimeComponents.cs:51-74 |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/inputs.json |

**相关文档**：[ai-02 PRD](../prd/ai-02-inputs.md) · [ai-03 reference](ai-03-norm-curves.md)
