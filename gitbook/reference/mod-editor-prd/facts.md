# 事实与取值表（生成物）

> 由 `scripts/generate-prd-facts.py` 从代码与资产抽取；**勿手改**，再生成本页。
> 生成时间：2026-08-17T00:59:56+08:00

## 配置目录（assets/config_catalog.json）

- 条目总数：**83**
- 按域：AI 18、Presentation 15、GAS 14、Input 10、Progression 3、Physics2D 3、Navigation 3、Items 3、UI 3、Narrative 3、Vision 1、Engine 1、Entities 1、Exchange 1、Relationships 1、Camera 1、Quests 1、EntityInfo 1
- 启用分片的表：**5** 张
  - `GAS/effects.json` → 分片目录 `GAS/effects`
  - `GAS/abilities.json` → 分片目录 `GAS/abilities`（AllowEmpty）
  - `GAS/graphs.json` → 分片目录 `GAS/graphs`
  - `GAS/preset_types.json` → 分片目录 `GAS/preset_types`
  - `Presentation/presenters.json` → 分片目录 `Presentation/presenters`（AllowEmpty）

## 游戏配置基线（assets/game.json）

- `targetFps`：0（代码默认 60）
- 窗口：1280×720，resizable=True
- 仿真预算：4ms/帧，最大切片 120
- 世界：cellSize 100cm，宏格 64×64
- gasRuntimeCapacity 共 **17** 项：
  - `abilityExecSnapshotCapacity` = 16384
  - `effectLifetimeSnapshotCapacity` = 16384
  - `effectFanOutCommandCapacity` = 16384
  - `orderQueueCapacity` = 4096
  - `responseChainOrderQueueCapacity` = 4096
  - `orderAdmissionResultCapacity` = 8192
  - `orderAdmissionRejectionCapacity` = 4096
  - `orderTerminalResultCapacity` = 4096
  - `deferredTriggerActiveEntityCapacity` = 16384
  - `projectileCollisionCandidateCapacity` = 16384
  - `projectileRuntimeEntityCapacity` = 16384
  - `effectPhaseGraphProgramScratchCapacity` = 16384
  - `graphOutputValueCapacity` = 16384
  - `abilityExecMaxWorkUnitsPerSlice` = 4096
  - `effectProcessingMaxWorkUnitsPerSlice` = 4096
  - `commandIntentScratchCapacity` = 4096
  - `effectRequestQueueCapacity` = 4096
  - 交叉约束（代码校验）：`orderAdmissionResultCapacity ≥ orderQueueCapacity × 2`、`orderAdmissionRejectionCapacity ≥ orderQueueCapacity`；两项工作预算（`abilityExecMaxWorkUnitsPerSlice`、`effectProcessingMaxWorkUnitsPerSlice`）另校验有限。

## GAS 运行时常量上限（src/Core/Gameplay/GAS/GasConstants.cs）

- `MAX_DEPTH` = 5
- `MAX_CREATES_PER_ROOT` = 256
- `MAX_RESPONSE_STEPS_PER_WINDOW` = 5000
- `MAX_RESPONSES_PER_WINDOW` = 4096
- `MAX_EFFECT_PROCESSING_PASSES_PER_FRAME` = 64
- `MAX_BLACKBOARD_ENTRIES` = 32
- `MAX_CHILDREN_BUFFER_CAPACITY` = 16
- `MAX_TAG_RULE_TRANSACTION_STEPS` = 256
- `MAX_PROCESSED_SET_CAPACITY` = 256
- `MAX_DEFERRED_TRIGGERS_PER_FRAME` = 1024
- `MAX_GAMEPLAY_EVENTS_PER_FRAME` = 4096
- `MAX_EFFECT_REQUESTS_PER_FRAME` = 4096
- `EFFECT_MODIFIERS_CAPACITY` = 8
- `EFFECT_CONFIG_PARAMS_MAX` = 32
- `EFFECT_GRANTED_TAGS_MAX` = 8
- `ACTIVE_EFFECT_CONTAINER_CAPACITY` = 32
- `EFFECT_PHASE_LISTENER_CAPACITY` = 8
- `GLOBAL_PHASE_LISTENER_MAX` = 32

## 关键注册表容量（源码常量）

- Tag 总数上限：`src/Core/Gameplay/GAS/TagRuleRegistry.cs` = **256**
- 属性总数上限：`src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs` = **64**
- 图程序上限：`src/Core/NodeLibraries/GASGraph/Host/GraphIdRegistry.cs` = **0**

