# ab-07 reference · 形态路由

> 现状参考。第一性需求见 [ab-07 PRD](../prd/ab-07-form-sets.md)；配置说明见 [ab-07 配置说明](../config/ab-07-form-sets.md)。

## 1. 现状快照

- 每帧路由：查同时持有三组件（状态/形态槽缓冲/路由集）的实体 → 形态槽 ClearAll → 遍历路由匹配（requiredAll ContainsAll ∧ blockedAny ¬Intersects，有效视角）→ priority 严格更大才替换（平分先出现者胜，无加载期同分校验）→ SetOverride（槽号 ≥8 跳过）。
- 缺形态槽缓冲的 actor 静默无路由。
- 加载器：routes 非空、requiredAll/blockedAny 掩码 + priority 必填 + slotOverrides 非空；slotIndex 0..7、同路由重复槽号拒、abilityId 须已注册；加载后 Freeze。
- 真实表项仅一份：jayce_forms（锤形态 requiredAll 单 tag、priority 100、4 槽覆盖）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 每帧路由循环 | src/Core/Gameplay/GAS/Systems/AbilityFormRoutingSystem.cs:28-93 |
| 表加载与校验/冻结 | src/Core/Gameplay/GAS/Config/AbilityFormSetConfigLoader.cs:21-157 |
| 形态槽层缓冲 | src/Core/Gameplay/GAS/Components/AbilityStateBuffer.cs:181-242 |
| 真实实例 | mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/ability_form_sets.json |

**相关文档**：[ab-07 PRD](../prd/ab-07-form-sets.md) · [ab-06 reference](ab-06-slots.md)
