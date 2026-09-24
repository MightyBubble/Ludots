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
