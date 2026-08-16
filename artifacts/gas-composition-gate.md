# GAS Composition Gate — Desert Strike Tug of War

## GAS Composition Gate — Self Review

- **Task / Issue**: 新增 showcase Mod「沙漠风暴（Desert Strike）」tug of war：波次出兵 + 自动交战 + 购买经济 + 基地胜负。
- **Date**: 2026-08-16
- **Agent / Author**: deepseek-v4-pro (agent-session-20260816 worktree)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **B（新增 graph 节点/effect 步骤的组合）+ Mod 层系统**。不新增 enum/preset/开关。

结论: **PASS**

一句话理由: 全部新玩法 = 现有 effect preset（InstantDamage）+ 现有能力 exec 项（TagClip/TagSignal/EffectSignal）组合出的**新 effect 步骤/新能力**，加上 Mod 自有系统消费既有标签/队列/属性管线；未触碰 BuiltinHandler、EffectPresetType、任何 profile schema。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 攻击伤害 | Layer 2（effect 步骤） | `Effect.Ds.Damage.*`（presetType InstantDamage） |
| 攻击节奏 | Layer 2（能力时间轴） | `Ability.Ds.Attack.*`：TagClip GCD + EffectSignal |
| 购买指令 | Layer 2（能力时间轴） | `Ability.Ds.Buy.*`：TagSignal（复用 RtsDemo 同款消费模式） |
| 波次出兵 | Layer 2（Mod 系统） | `DesertStrikeWaveSystem` → `RuntimeEntitySpawnQueue`（引擎官方运行时生成通道） |
| 索敌/推进 | Layer 2（Mod 系统） | `DesertStrikeAutoBattleSystem`（蓝本 `CombatStanceOrderSystem`，全 API 已验证） |
| 经济 | Layer 2（Mod 系统） | `DesertStrikePurchaseSystem`/`IncomeSystem` → `AttributeMutationOps.SetCurrent` |
| 死亡/胜负 | Layer 2（Mod 系统） | `DesertStrikeDeathSystem`（narrative showcase 同款轮询模式） |

### 3. Reuse list

- Handlers: InstantDamage（EffectProposalProcessingSystem 既有分发）；TagSignal/TagClip（AbilityExecLoader 既有 exec 项）；TagOps（Effective 判定）
- Queues / Systems: OrderQueue（castAbility/moveTo）、RuntimeEntitySpawnQueue + RuntimeEntitySpawnReceiptQueue、MoveToWorldCmOrderSystem、EffectRequestQueue（经能力链路）
- Resolvers / Registries: TagRegistry、AttributeRegistry、OrderTypeRegistry（game.json constants）、ComponentRegistry（自定义组件 authoring）、ISpatialQueryService
- Existing presets / graphs: 无新 graph；preset 仅 InstantDamage 复用

### 4. New Layer 0 ops (if any)

N/A——未新增任何 Layer 0 op、BuiltinHandler、EffectPresetType。

### 5. Transaction boundary

必须原子 rollback 的步骤: 购买扣费与入队（同一系统同帧完成：余额校验 → SetCurrent 扣费 → 入队；余额不足直接拒绝，不产生中间态）。波次生成无回滚需求（生成失败即抛错中止帧）。

### 6. Config SSOT

行为配置落在: Mod 自有 `assets/Configs/desert_strike_config.json`（VFS 直读，MobaConfig 同款）+ GAS effect/ability catalog（`assets/GAS/*.json`）+ 实体模板属性（`assets/Entities/templates.json`）。

是否新增 JSON schema: **NO**——`desert_strike_config.json` 是 Mod 私有配置（非 GAS catalog schema，不进 ConfigCatalog），无新 schema 注册。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **effect 步骤 / Mod 系统**（如新单位 = 新模板 + 新攻击能力/效果 + config 一行；新经济规则 = 改 Mod 系统）

若选了 Core enum → FAIL（未选，PASS）
