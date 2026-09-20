# ab-04 reference · 冷却三件套

> 现状参考。第一性需求见 [ab-04 PRD](../prd/ab-04-cooldown.md)；配置说明见 [ab-04 配置说明](../config/ab-04-cooldown.md)。

## 1. 现状快照

- AbilityCooldown 组件只是数据契约（两个 int：CooldownValueAttributeId/CooldownTagId）；全 Core 无系统写它；消费方 = 加载器编译 + AbilityDefinition 字段 + UtilityAi 就绪检查。
- JSON cooldown 块零使用：全部 mods 的 abilities.json 无一处 "cooldown" 声明；实战冷却全部 TagClip+blockTags。
- 闭环路径：起播 FireTagClip 加 tag 并向 TimedTagBuffer 预约到期（含失败回滚）；TimedTagExpirationSystem 到期 RemoveTag；下次激活被 blockTags 拒绝。
- AI 就绪：决策级 SharedCooldownTagId 优先，否则取技能 CooldownTagId；valueAttribute Current>0→未就绪（AbilityCooldown 原因）、冷却 tag 在场→未就绪（SharedCooldown 原因）；AI 提交后写 SharedCooldownUntilStep。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 契约组件（两 int） | src/Core/Gameplay/GAS/Components/AbilityCooldown.cs:3-8 |
| cooldown 块编译 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:322-364 |
| TagClip 挂 tag+预约 | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:1216-1255 |
| 定时 tag 到期移除 | src/Core/Gameplay/GAS/Systems/TimedTagExpirationSystem.cs:43-68 |
| blockTags 拒激活 | src/Core/Gameplay/GAS/Systems/AbilityActivationBlockTagEvaluator.cs:8-22；AbilityExecSystem.cs:236-257 |
| AI 就绪判定 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:602-675 |
| AI 共享冷却窗口写入 | src/Core/Gameplay/AI/Utility/UtilityAiSystems.cs:201-205 |
| 真实实例 | mods/showcases/champion_skill_sandbox/.../abilities.json；mods/showcases/utility_autocast/.../abilities.json |

**相关文档**：[ab-04 PRD](../prd/ab-04-cooldown.md) · [ab-05 reference](ab-05-activation-gates.md)
