# GAS Composition Gate - PR #658 / Issue #690

- Date: 2026-07-19
- Agent: Codex
- Result: PASS

## Core judgment

主要交付物：A，复用现有 Command Router、OrderQueue、OrderBuffer、GAS system phase 和 typed MovePlan contract，删除 MassNavigation/Formation 的平行 Order consumer。

本次没有新增 effect preset、profile enum、graph op、lifecycle DSL 或平行 loader。`MovePlanExecutionMode` 是中立执行端口的显式类型边界，不是 GAS 玩法变体开关。

## Layer assignment

| 能力 | Layer | 实现载体 |
| --- | --- | --- |
| cluster actor expansion | Input/Command Router extension | `ICommandActorExpander` / `FormationCommandActorExpander` |
| atomic admission and activation | existing GAS order infrastructure | `OrderQueue` / `OrderBufferSystem` |
| Order to typed movement | GAS adapter | `MovePlanOrderProjectionSystem` |
| typed movement execution | MovePlanning port + Mass adapter | `MovePlanExecutionIntent` / `MassNavigationMovePlanExecutionSystem` |
| typed result to lifecycle | GAS adapter | `MovePlanOrderLifecycleSystem` |

## Reuse list

- Handlers: existing order type registry and order rules; no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderSubmitter`, existing `SystemGroup.AbilityActivation`.
- Resolvers / Registries: `CommandIntentProfileRegistry`, `CastDispatchProfileRegistry`, `OrderTypeRegistry`, `ControlDomainQuery`.
- Existing contracts: `MovePlanExecutionIntent`, `IMovePlanExecutionSink`, `MassNavigationRuntimeBinding`.

## New Layer 0 ops

N/A. No entity lifecycle atomic op was added.

## Transaction boundary

- Command Router fan-out validates expansion capacity and submits one clustered batch.
- `OrderBufferSystem` previews every row before activating any row.
- Mass command-group execution prepares final resolved destination and member targets once, validates binding/focus/route capacity against that exact data, then commits the same prepared targets without recomputation.
- Route rejection emits typed failure; GAS cancels the matching order and removes its continuation.

## Config SSOT

- Order catalog: `mods/capabilities/navigation/MassNavigationMod/assets/GAS/order_types.json`
- Formation business data: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/FormationCapabilityShowcaseConfig.json`
- Input routing: `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/Input/`
- Mass capacities: each Mod's `MassNavigationConfig.json`

新增 JSON schema: NO. Renamed dead ingestion capacity fields to typed MovePlan execution capacity fields and removed the unused `orderIdleScanIntervalFrames` property.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建 spawn/order/MovePlan 平行管线
- [x] 未添加 fallback 或兼容旁路
- [x] MassNavigation 不读取或反写 Order
- [x] Formation 不拥有专用 Order consumer
- [x] 热路径容量显式，容量不足失败

## Next variant test

---

# GAS Composition Gate - Graph AI 50k Stress Field

- Date: 2026-07-25
- Agent: Codex
- Result: PASS

## Core judgment

New variant primary delivery (A/B/C/D): A

Conclusion: PASS

Reason: This adds a standalone showcase that composes existing graph instructions and the existing Arch ECS inline query path. It does not add a graph op, GAS preset, lifecycle DSL, profile enum, or parallel AI loader.

## Layer assignment

| Step / capability | Layer | Carrier |
| --- | --- | --- |
| 50k FSM decisions | Layer 2 graph composition | `stress_field_fsm` program |
| 50k BT task decisions | Layer 2 graph composition | `stress_field_bt` program |
| ECS hot path | Runtime/system reuse | Arch `InlineEntityQuery` over `GraphAiStressBrain` / `GraphAiStressIntent` |
| Visible stress field | Showcase presentation | `PrimitiveDrawBuffer` mesh dots driven by ECS intent state |

## Reuse list

- Handlers: existing `GraphExecutor`, `GraphInstruction`, and Graph AI op handler table.
- Queues / Systems: existing map lifecycle callbacks, `SystemGroup.InputCollection`, and presentation system registration.
- Resolvers / Registries: existing mod config pipeline, map loader, launcher binding, showcase registry.
- Existing presets / graphs: FSM and BT behavior are graph programs in the showcase catalog; no Core enum is added.

## New Layer 0 ops

N/A.

## Transaction boundary

N/A. Stress entities are created once on map focus and destroyed on map unload. The hot path only mutates SoA arrays and ECS intent components; it does not perform structural changes.

## Config SSOT

Behavior configuration lives in:

- `mods/showcases/graph_stress_field/GraphStressFieldShowcaseMod/assets/GraphAiShowcase/showcase.json`

New JSON schema: NO for GAS/core gameplay schema. The existing showcase-only `GraphAiShowcaseConfig` gets stress display fields and validates them fail-fast.

## Red flag scan

- [x] No profile inherit/placement enum
- [x] No parallel spawn/materialization pipeline
- [x] No lifecycle placement validation shortcut
- [x] No silent default or unclear fallback
- [x] No hot-path structural changes
- [x] No memory flying wires in the ECS tick path

## Next variant test

The next Mod variant should modify graph wiring or showcase-owned graph config, not a Core enum.
下一个 Formation Mod 变体应修改 Mod-owned anchor/member 数据和 `ICommandActorExpander` 实现，继续复用同一 Command Router、GAS Order 与 typed MovePlan 链；不得新增 Core Formation enum 或专用 order pipeline。

---

# GAS Composition Gate - StarCraft Full Showcase closure

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次只修正 showcase runtime 对已有 AttributeRegistry、EffectTemplateIdRegistry、EffectRequestQueue 与 GAS Effect Pipeline 的使用方式，不新增 profile enum、preset 开关、JSON schema、graph op 或平行管线。

## Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Assault Hatchery 场景信号 | Layer 2 composition caller | 现有 ability exec 发布 `Effect.Scf.Command.AssaultHatchery`，场景系统消费 `AssaultSignal` |
| Terran volley damage | Layer 2 composition caller | 现有 `EffectRequestQueue` 发布 `Effect.Scf.Damage.*` |
| Health / Minerals / AssaultSignal 读取 | Registry reuse | `AttributeRegistry` 已注册 id |
| Damage effect id 查找 | Registry reuse | `EffectTemplateIdRegistry` |

## Reuse list

- Handlers: existing GAS effect handlers and graph phase execution.
- Queues / Systems: `EffectRequestQueue`, `EffectProcessingLoopSystem`, current showcase `RtsScFullScenarioSystem`.
- Resolvers / Registries: `AttributeRegistry`, `EffectTemplateIdRegistry`, `EntityTemplateKeyRegistry`.
- Existing presets / graphs: `Effect.Scf.Damage.*`, `Effect.Scf.Mining.Minerals`, `Graph.Scf.*` from the StarCraft full showcase assets.

## New Layer 0 ops (if any)

N/A.

## Transaction boundary

必须原子 rollback 的步骤: N/A. The showcase assault loop publishes ordinary GAS damage requests; no entity lifecycle transaction is introduced.

## Config SSOT

行为配置落在: effect template / graph / catalog（路径）: `mods/showcases/rts_starcraft_full/RtsStarCraftFullShowcaseMod/assets/GAS/effects.json` and `mods/showcases/rts_starcraft_full/RtsStarCraftFullShowcaseMod/assets/GAS/graphs.json`.

是否新增 JSON schema: NO.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

## Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

---

# GAS Composition Gate - Graph AI showcase triad

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次交付三个独立 Graph AI showcase 和一个 50k SoA benchmark，行为差异都落在 graph program 连线、寄存器输出和 task countdown 上；没有新增 profile enum、preset 开关、lifecycle DSL、graph op 或平行 AI loader。

## Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 关卡蓝图阶段推进 | Layer 2 composition | `graph_level_blueprint` 的 `level_blueprint_opening` graph program |
| RTS stance FSM | Layer 2 composition | `graph_stance_fsm` 的 `rts_stance_fsm` graph program |
| 复杂 BT task 选择与倒计时 | Layer 2 composition | `graph_complex_bt` 的 `complex_bt_selector` graph program + runtime task countdown |
| 玩家可见舞台 | Showcase presentation | 三个 showcase 各自的 map actors、performer assets 和 overlay stage |
| 50k FSM/BT 热路径证据 | Test/benchmark | `GraphAiFsmBtBenchmarkTests` 预分配 SoA register arrays + Arch inline query |

## Reuse list

- Handlers: existing `GraphExecutor` / `GraphInstruction` handler-table model; no new GAS BuiltinHandler.
- Queues / Systems: `GameEngine` map lifecycle, `ScreenOverlayBuffer`, Arch `InlineEntityQuery` in benchmark.
- Resolvers / Registries: existing config pipeline, map loader, entity template registry, performer registry.
- Existing presets / graphs: no old RTS/C&C/CombatStance showcase graph is imported; the three new showcase graph programs are isolated in each Mod.

## New Layer 0 ops (if any)

N/A. No lifecycle atomic op, GAS op, or graph op was added.

## Transaction boundary

必须原子 rollback 的步骤: N/A. The showcases do not create gameplay lifecycle transactions; maps load ordinary showcase marker entities and graph programs only produce local showcase state.

## Config SSOT

行为配置落在: graph / showcase catalog（路径）:

- `mods/showcases/graph_level_blueprint/GraphLevelBlueprintShowcaseMod/assets/GraphAiShowcase/showcase.json`
- `mods/showcases/graph_stance_fsm/GraphStanceFsmShowcaseMod/assets/GraphAiShowcase/showcase.json`
- `mods/showcases/graph_complex_bt/GraphComplexBtShowcaseMod/assets/GraphAiShowcase/showcase.json`

是否新增 JSON schema: NO for GAS/core gameplay schema. The showcase-only catalog uses the existing local `GraphAiShowcaseConfig` model and is validated fail-fast.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未在旧 RTS/C&C/CombatStance showcase 上叠改
- [x] 三个 showcase 分别拥有独立 mod、map、preset、registry entry 和验收断言

## Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

---

# GAS Composition Gate - Frontline consume defeated unit

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: Frontline defeated-unit cleanup needs a lifecycle graph composition that consumes the source entity; this reuses `RuntimeEntityLifecycleQueue`, `EffectRequestQueue`, graph phase execution, and the existing `ConsumeEntity` atomic handler instead of adding a profile enum or bypass destroy path.

## Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Begin consume-source transaction | Layer 1 thin transaction entry | `BeginLifecycleConsumeSource` graph op |
| Consume defeated unit | Layer 0 existing op | existing `ConsumeEntity` builtin handler |
| Mod cleanup request | Layer 2 composition caller | `Effect.Rts.Frontline.ConsumeDefeatedUnit` |

## Reuse list

- Handlers: existing `ConsumeEntity`.
- Queues / Systems: `RuntimeEntityLifecycleQueue`, `RuntimeEntityLifecycleSystem`, `EffectRequestQueue`, `EffectProcessingLoopSystem`.
- Resolvers / Registries: `EffectTemplateIdRegistry`, `AttributeRegistry`.
- Existing presets / graphs: existing lifecycle transaction state and graph builtin invocation.

## New Layer 0 ops (if any)

N/A.

## Transaction boundary

必须原子 rollback 的步骤: source consume only; no materialized target exists, so rollback does not need to destroy a created target.

## Config SSOT

行为配置落在: effect template / graph / catalog（路径）: `assets/Configs/GAS/graphs.json`, `mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod/assets/GAS/effects.json`, and `mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod/assets/RtsMultiplayerFrontlineConfig.json`.

是否新增 JSON schema: NO.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

## Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤

---

# GAS Composition Gate - Graph AI showcase visible motion repair

- Date: 2026-07-24
- Agent: Codex
- Result: PASS

## Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次不新增 graph op、GAS preset、profile enum 或生命周期管线，只把已有 showcase graph 输出绑定到地图实体位置、朝向和预分配 SoA 热路径计数，玩家看到的是已有 graph 决策投射到实体运动。

## Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 关卡蓝图流程可视化 | Layer 2 composition viewer | `level_blueprint_opening` 输出阶段，runtime 移动地图 stage/cursor 实体 |
| RTS stance 可视化 | Layer 2 composition viewer | `rts_stance_fsm` 输出 stance/intent，runtime 移动已绑定 squad 实体 |
| BT task 可视化 | Layer 2 composition viewer | `complex_bt_selector` 输出 task/duration，runtime 移动已绑定 actor 实体 |
| 50k 热路径可见证据 | Benchmark/probe | 预分配 SoA register arrays + existing `GraphExecutor` handler-table contract |

## Reuse list

- Handlers: existing `GraphExecutor` / `GraphInstruction`; no new GAS BuiltinHandler or graph op.
- Queues / Systems: existing map lifecycle callbacks, `SystemGroup.InputCollection`, presentation overlay system.
- Resolvers / Registries: existing map `EntityIndex`, template registry, performer bootstrap.
- Existing presets / graphs: three showcase-owned graph programs only.

## New Layer 0 ops (if any)

N/A.

## Transaction boundary

必须原子 rollback 的步骤: N/A. 本次不创建、销毁或 morph 实体，只写已有实体的 `WorldPositionCm` / `PreviousWorldPositionCm` / `FacingDirection`。

## Config SSOT

行为配置落在: graph / showcase catalog（路径）:

- `mods/showcases/graph_level_blueprint/GraphLevelBlueprintShowcaseMod/assets/GraphAiShowcase/showcase.json`
- `mods/showcases/graph_stance_fsm/GraphStanceFsmShowcaseMod/assets/GraphAiShowcase/showcase.json`
- `mods/showcases/graph_complex_bt/GraphComplexBtShowcaseMod/assets/GraphAiShowcase/showcase.json`

是否新增 JSON schema: NO for GAS/core gameplay schema. Showcase-only binding fields are validated fail-fast and do not define gameplay variants.

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

## Next variant test

「下一个 Mod 变体」将修改: graph 连线 / showcase entity binding

---

# GAS Composition Gate - Graph Level Blueprint trigger repair

- Date: 2026-07-27
- Agent: Codex
- Result: PASS

## Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次把关卡蓝图 showcase 从时间自动播放改为玩家 token 进入触发区后由现有 graph program 推进；不新增 graph op、GAS preset、profile enum、生命周期 DSL 或平行触发管线。

## Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 玩家 token 移动 | Showcase runtime input binding | `PlayerInputHandler.ReadAction<Vector2>("Move")` |
| 触发条件输入 | Layer 2 composition input | `level_blueprint_opening` 读取 trigger-ready register |
| 关卡动作推进 | Layer 2 graph composition | 现有 `CopyInt` / `CompareEqIntImm` / `JumpIfFalse` / `ConstInt` 指令组合 |
| 世界反馈 | Showcase presentation | 已有地图实体的 `WorldPositionCm` / `PreviousWorldPositionCm` / `FacingDirection` |

## Reuse list

- Handlers: existing `GraphExecutor`, `GraphInstruction`, and Graph AI op handler table.
- Queues / Systems: existing map lifecycle callbacks, `SystemGroup.InputCollection`, and presentation overlay system.
- Resolvers / Registries: existing map `EntityIndex`, input config pipeline, template registry, performer bootstrap.
- Existing presets / graphs: `Default_Gameplay` `Move` action and showcase-owned `level_blueprint_opening` graph program.

## New Layer 0 ops (if any)

N/A.

## Transaction boundary

必须原子 rollback 的步骤: N/A. 本次不创建、销毁、morph 实体，也不发起 GAS 生命周期事务；只在 showcase runtime 内移动既有实体并把触发命中作为 graph 输入。

## Config SSOT

行为配置落在: graph / showcase catalog（路径）:

- `mods/showcases/graph_level_blueprint/GraphLevelBlueprintShowcaseMod/assets/GraphAiShowcase/showcase.json`

是否新增 JSON schema: YES for showcase-only `GraphAiShowcaseConfig.LevelFlow` fields (`moveActionId`, `cursorSpeedCmPerSecond`, `triggerRadiusCm`). 这些字段只描述展示输入绑定与触发半径，不定义 Core/GAS 玩法变体；graph 推进仍由现有 op 组合表达。

## Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未新增 Core trigger pipeline 或 graph op

## Next variant test

「下一个 Mod 变体」将修改: graph 连线 / showcase entity binding / showcase trigger tuning
