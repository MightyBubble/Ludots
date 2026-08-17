# fx-17 reference · 效果授予 Tag

> 现状参考。第一性需求见 [fx-16 PRD](../prd/fx-13-granted-tags.md)；配置说明见 [fx-16 配置说明](../config/fx-13-granted-tags.md)。

## 1. 现状快照

- 公式 Fixed/Linear/LinearPlusBase 三实现；GraphProgram 在 loader 即抛 "until a tag contribution graph evaluator is wired"，其后参数处理与图解析代码保留但不可达。
- amount/base 编译期钳到 ushort；单效果授予条数上限 `EFFECT_GRANTED_TAGS_MAX`（事实页）。
- 堆叠刷新按 Compute(new)−Compute(old) 差量调 TagOps；失败先回滚 stack/GameplayEffect 再上抛。
- Grant/Revoke/Update 三对方法（实体版 + TagCountContainer 版）全量 before 快照回滚；容器计数满抛 `GAS.TAG.ERR.TagCountOverflow` 并计预算，实体版规则拒绝抛 `GAS.TAG.ERR.RuleRejected`。
- 应用期授予走 StageGrantedTagGrant；过期/移除走 StageGrantedTagRevoke，层数取移除时 EffectStack.Count（无则 1）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 公式编译与 GraphProgram 拒绝 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1961-2024 |
| 差量合并与回滚 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1660-1678 |
| 三对 Grant/Revoke/Update | src/Core/Gameplay/GAS/EffectTagContributionHelper.cs:13-200 |
| 容量满计预算 | EffectTagContributionHelper.cs:143-147,191-195 |
| 应用期授予 stage | src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs:417 |
| 过期回收 stage | src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs:663 |
| 授予容量常量 | src/Core/Gameplay/GAS/GasConstants.cs:54 |
| 公式计算测试 | src/Tests/GasTests/GasCore/TagEffectArchitectureTests.cs:96-110 |

**相关文档**：[fx-16 PRD](../prd/fx-13-granted-tags.md) · [fx-16 配置说明](../config/fx-13-granted-tags.md)
