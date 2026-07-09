## GAS Composition Gate - Self Review

- **Task / Issue**: PR #581 RFC-0065 A4 axis-move follow-up and SHOW-6 WASD hot-switch evidence.
- **Date**: 2026-07-07
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次复用既有 `ControlSchemeRuntime`、`InputConfigPipelineLoader`、`AxisMoveOrderSystem`、`OrderQueue` 与 `interaction_showcase`，只新增既有 `control_schemes.json` schema 下的数据实例和 fail-fast 引用校验；没有新增 profile enum、preset 开关、graph op、effect step 或平行 loader。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| axisMove action/order validation | N/A - existing input/control infrastructure | ControlSchemeRuntime + InputConfigRoot + OrderTypeRegistry |
| SHOW-6 WASD scheme data | N/A - existing config schema instance | InteractionShowcaseMod/assets/Input/control_schemes.json |
| production hot-switch evidence | N/A - test evidence | Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests |

### 3. Reuse list

- Handlers: N/A, no GAS handler changes.
- Queues / Systems: existing AxisMoveOrderSystem and OrderQueue.
- Resolvers / Registries: existing ControlSchemeRuntime, InputConfigPipelineLoader, OrderTypeRegistry, CommandIntentProfileRegistry.
- Existing presets / graphs: N/A, no preset or graph changes.

### 4. New Layer 0 ops (if any)

N/A - no new atomic op.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A. Control-scheme switching is runtime preference/context bookkeeping; no gameplay lifecycle transaction is introduced.

### 6. Config SSOT

行为配置落在: existing `Input/control_schemes.json` schema, mod-owned fragment `mods/showcases/interaction/InteractionShowcaseMod/assets/Input/control_schemes.json`.

是否新增 JSON schema: NO - uses existing `axisMove` declaration fields.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: existing control scheme data entries, not Core enum.

---

## GAS Composition Gate - Self Review

- **Task / Issue**: PR #581 RFC-0065 workflow closeout, benchmark hardening, and review-gap verification for relationship/control-plane hot paths.
- **Date**: 2026-07-06
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次修复复用既有 RelationshipRuntime、AssociationControlProfileRuntime、KnowledgeCommandTargetGate、DomainRoutedCollectionWriter、ControlPlaneView 和测试预算护栏；没有新增 profile enum、preset 开关、graph op、effect step 或平行 schema。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| relationship typed edge churn 0Alloc | N/A - existing runtime infrastructure | RelationshipRuntime / Relationship<T> / RelationshipEdgeSet |
| association profile physical grant/revoke budget | N/A - existing runtime infrastructure | AssociationControlProfileRuntime + GasTests budget |
| command target knowledge gate fail-fast | N/A - input/control infrastructure | KnowledgeCommandTargetGate injection into CoreInput and registries |
| partial-domain projection budget | N/A - collection/control infrastructure | ControlPlaneViewUnitGrantTests |
| domain-routed write flatness budget | N/A - collection/control infrastructure | DomainRoutedCollectionTests |
| workflow closeout audit | N/A - documentation evidence | docs/audits/rfc_0065_pr581_workflow_closeout.md |

### 3. Reuse list

- Handlers: N/A, no GAS handler changes.
- Queues / Systems: existing AssociationControlProfileSystem path in SchemaUpdate; existing OrderQueue remains untouched.
- Resolvers / Registries: RelationshipRuntime, RelationshipReverseIndex, ControlDomainQuery, KnowledgeProjectionResolver, CommandIntentProfileRegistry, ContextScoredOrderResolver.
- Existing presets / graphs: N/A, no preset or graph changes.

### 4. New Layer 0 ops (if any)

N/A - no new atomic op.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A. Relationship mutations remain through existing EnsureLink/RemoveLink/SetFlag paths; no new multi-step lifecycle transaction is introduced.

### 6. Config SSOT

行为配置落在: existing RFC-0065 catalog/profile assets and runtime registries.

是否新增 JSON schema: NO - no new schema.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤 / existing data profile entries, not Core enum.

---

## GAS Composition Gate - Self Review

- **Task / Issue**: PR #581 RFC-0065 A2 entity command panel showcase M6/P3 aggregation profile switching.
- **Date**: 2026-07-06
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次只复用既有 entity command panel collection source、AbilityAggregationProfileRegistry、ConfigPipeline ArrayById 与 ability catalog tags；没有新增 handler、effect preset、graph op、profile field、schema、Core enum 或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| M6 showcase command-owner collection | N/A - UI/showcase data projection | EntityCommandPanelShowcaseRuntime + EntityCollectionStore |
| P3 runtime aggregation profile switching | N/A - existing UI runtime preference | CollectionGasEntityCommandPanelSource.SetAggregationProfile |
| Family grouping metadata | N/A - existing ability catalog metadata | EntityCommandPanelShowcaseMod/assets/GAS/abilities.json catalogTags |
| Production acceptance | N/A - test evidence | GasTests Production acceptance |

### 3. Reuse list

- Handlers: N/A, no GAS handler changes.
- Queues / Systems: existing EntityCommandPanelPresentationSystem, EntityCollectionPresentationEventSystem, Mod Loading, ConfigPipeline.
- Resolvers / Registries: AbilityAggregationProfileRegistry, AbilityDefinitionRegistry, EntityCommandPanelSourceRegistry, EntityCollectionStore.
- Existing presets / graphs: N/A, no preset or graph changes.

### 4. New Layer 0 ops (if any)

N/A - no new atomic op.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A. Profile switching is a UI source preference update; no gameplay lifecycle transaction is introduced.

### 6. Config SSOT

行为配置落在: existing `GAS/abilities.json` ArrayById catalog metadata and existing `UI/ability_aggregation_profiles.json` profile definitions.

是否新增 JSON schema: NO - uses existing ability `catalogTags` and existing aggregation profile schema.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: existing ability catalog tags or existing aggregation profile entries, not Core enum.

---

## GAS Composition Gate - Self Review

- **Task / Issue**: PR #581 RFC-0065 selection-retirement closeout: AbilityExecLoader fail-fast tightening.
- **Date**: 2026-07-09
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 本次只收紧既有 ability config loader 的校验边界，拒绝坏配置静默通过；没有新增 handler、effect preset、graph op、profile enum、JSON schema 或平行加载管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Ability exec config validation | N/A - existing config boundary | `AbilityExecLoader` |
| GraphSignal id resolution | N/A - existing registry lookup | `GraphIdRegistry.GetId` |
| Presentation mode override validation | N/A - existing enum contract | `InteractionModeType` |
| Regression coverage | N/A - tests | `AbilityExecLoaderFailFastTests` |

### 3. Reuse list

- Handlers: N/A, no GAS handler changes.
- Queues / Systems: existing AbilityExec runtime only; no new runtime queue.
- Resolvers / Registries: `EffectTemplateIdRegistry`, `GraphIdRegistry`, `TagRegistry`, `ConfigKeyRegistry`.
- Existing presets / graphs: Existing graph ids must already be registered; loader no longer registers unknown graph names from ability items.

### 4. New Layer 0 ops (if any)

N/A - no new atomic op.

### 5. Transaction boundary

必须原子 rollback 的步骤: N/A. This is config compilation fail-fast behavior, not a gameplay lifecycle transaction.

### 6. Config SSOT

行为配置落在: existing `GAS/abilities.json` schema and existing registries.

是否新增 JSON schema: NO - existing fields are validated more strictly.

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: ability JSON entries / graph assets / effect templates, not Core enum.
