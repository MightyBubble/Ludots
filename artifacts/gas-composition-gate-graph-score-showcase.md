# GAS Composition Gate — GraphScore 残血优先短剧

本票自审。不覆盖 `artifacts/gas-composition-gate.md`（S13 落地正本）。

## 任务摘要

给已有打分图配一间玩家短剧：场上同时站满血木桩和残血木桩。选人输入走已有 `GraphScore`，打分图按「缺的血」打分，队友自动打分高的那个。字幕只读决策痕迹里的选中人和分。

不新增 graph op、不新增 profile enum、不新开打分执行门，也不在字幕/验收里再跑一遍打分图。

## 判断标准结论

**通过（A）**

新变体是已有 op 的连线：`LoadExplicitTarget` → `LoadAttribute Health` → `ConstFloat 100` → `SubFloat`，图 kind 为已有的 `Score`。选人只用已有 `GraphScore` 输入。扣血走已有 InstantDamage 效果模板。满血常数只写在打分图里（今天没有读生命上限的节点）。

## GAS Composition Gate — Self Review

- **Task / Issue**: 打分图走正式 graph 链路，并配可玩 showcase
- **Date**: 2026-08-14
- **Agent / Author**: Cursor Grok

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 只改 Score 图连线和选人配置，不新增 Core enum / preset 开关 / 平行打分管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 读目标当前生命 | 0 | `LoadAttribute` |
| 用满血常数减出缺口 | 0 | `ConstFloat` + `SubFloat` |
| 打分图 ABI（结果进 F[0]） | 1 | 已有 `GraphKind.Score`（由选人 `GraphScore` 调用） |
| 按分数选人并出手 | 2 | Utility `GraphScore` 输入 + `castAbility` + InstantDamage |
| 玩家短剧标题/字幕 | 3 | showcase overlay，只读 `UtilityAiDecisionTrace` |

### 3. Reuse list

- Handlers: 已有 graph op（LoadExplicitTarget / LoadAttribute / ConstFloat / SubFloat）
- Queues / Systems: Core Utility AI think/decision、GAS ability/effect
- Resolvers / Registries: `GraphProgramRegistry`、`AbilityDefinitionRegistry`、`AttributeRegistry`
- Existing presets / graphs: InstantDamage；玩家门是 Utility `GraphScore` → `UtilityAiDecisionTrace`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无新事务。一次普攻仍走既有 ability → effect 结算。打分图只读，禁止写属性/效果。

### 6. Config SSOT

行为配置落在: `mods/showcases/capability_standard/CapabilityStandardGraphScoreShowcaseMod/assets/GAS/graphs.json`（Score 图）、`assets/GAS/effects.json`（扣血）、`assets/Configs/AI/*.json`（按分数选人）

是否新增 JSON schema: **NO**

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未新增 `.GetEngine(`
- [x] 未新增未声明 `RegisterSystem(`
- [x] 未直接 `AttributeBuffer.SetBase/SetCurrent`
- [x] 字幕/验收未另开 `GraphExecutor.ExecuteScore` 门

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（例如改成距离衰减，或把常数 100 换成读 MaxHealth）

若选了 Core enum → FAIL
