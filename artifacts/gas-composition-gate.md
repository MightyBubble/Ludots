## GAS Composition Gate 鈥?Self Review

Current closeouts and prior issue reviews follow.

## PR #660 / #689 Final Gate Repair - Order Runtime Transactions - 2026-07-25

- **Task / Issue**: Close #689 gate blockers for PR #660 current head, covering order admission capacity, pending retry terminal outcomes, AbilityExec structural mutation rules, move-then-cast rejection semantics, collection command-panel aiming, runtime spawn transaction boundaries, and runtime capacity validation.
- **Date**: 2026-07-25
- **Agent / Author**: Codex.

### 1. Core judgment

Primary delivery: A. Tighten existing GAS order/input/spawn execution boundaries.

Result: PASS.

Reason: The repair reuses existing `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `OrderSubmitter`, `OrderContinuationSystem`, `AbilityExecSystem`, `CompositeOrderPlanner`, `InputOrderMappingSystem`, `GasEntityCommandPanelSource`, `CollectionGasEntityCommandPanelSource`, `RuntimeEntitySpawnSystem`, and `GasRuntimeCapacityConfig`. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, compatibility bypass, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Batch EntityIntake admission-capacity rejection | N/A | Existing `OrderAdmissionResultBuffer` rejection area and `OrderBufferSystem` batch intake |
| Runtime capacity fail-fast validation | N/A | Existing `GasRuntimeCapacityConfig.Validate` and default `assets/Configs/game.json` |
| Pending retry terminal outcome | N/A | Existing `OrderSubmitter.Preview`, `OrderSubmitter.Submit`, and `OrderTerminalResultBuffer` |
| AbilityExec hot-path structural mutation guard | N/A | Existing `CommandBuffer` playback plus ArchitectureTests guard |
| Move-then-cast plan outcome split | N/A | Existing `CompositeOrderPlanner` |
| Collection command panel multi-member aiming rejection | N/A | Existing command panel source and input mapping activation result |
| Runtime spawn single/batch preflight | N/A | Existing `RuntimeEntitySpawnSystem` relationship, receipt, presentation, and effect queues |
| Windows launcher output-drain test cleanup stability | N/A | Existing ArchitectureTests cleanup helper |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderContinuationSystem`, `OrderAdmissionResultBuffer`, `OrderTerminalResultBuffer`, `AbilityExecSystem`, `RuntimeEntitySpawnSystem`.
- Resolvers / Registries: `OrderTypeRegistry`, `OrderRuleRegistry`, `AbilityDefinitionRegistry`, `AbilityAggregationProfileRegistry`, existing relationship/team/ownership services.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch EntityIntake checks whole-batch result capacity before any per-row reservation. If regular result capacity is missing but rejection capacity can hold the batch, the system dequeues the whole batch, publishes `RejectedAdmissionCapacity` for every existing `OrderId`, releases payloads, and stops. If rejection capacity is also missing, it fails fast without dequeueing.

Pending retry now reserves admission and failed-terminal capacity before clearing pending or releasing payload. AbilityExec publishes terminal presentation events before queuing structural removal, and direct `World.Add/Remove<AbilityExecInstance>` is guarded out of the GAS hot path. Runtime spawn single and batch paths preflight relationship, owner/team, receipt, presentation, and on-spawn effect capacity before dequeue.

### 6. Config SSOT

Runtime capacity remains in `assets/Configs/game.json` and is validated by `GasRuntimeCapacityConfig.Validate`. `orderAdmissionResultCapacity` must cover the same generation's GlobalIntake + EntityIntake worst case, and `orderAdmissionRejectionCapacity` must cover a full queued batch rejection.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel order, input, ability, spawn, or effect runtime added
- [x] No compatibility fallback or silent bypass added
- [x] No accepted order path without terminal-result ownership remains in the repaired paths
- [x] No capacity failure path mutates authority before required capacity is reserved in the repaired paths
- [x] No direct `World.Add/Remove<AbilityExecInstance>` remains in the guarded GAS hot path

### 8. Verification

- `dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj -c Debug --no-restore --nologo`: PASS, 188/188.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~InputOrderAbilityAuditTests|FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~CollectionGasEntityCommandPanelAggregationTests|FullyQualifiedName~RoadNetworkShowcaseTests|FullyQualifiedName~OrderCompositePlannerTests" --nologo`: PASS, 198/198.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GasExecutionBudgetTests" --nologo --logger "console;verbosity=minimal"`: PASS, 34/34.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --filter "FullyQualifiedName~CollectionGasEntityCommandPanelAggregationTests" --nologo --logger "console;verbosity=minimal"`: PASS, 7/7.
- `git diff --check origin/main...HEAD`: PASS.

### 9. Next variant test

A new Mod order, input, ability, or runtime spawn variant changes data, graph wiring, or effect steps and continues through the same admission, terminal, command-panel activation, and spawn preflight contracts. It must not add a Core gameplay enum, fallback path, alternate order/spawn runtime, or hot-path structural mutation.

## PR #660 Final Integration Addendum - Runtime Spawn Batch Preflight - 2026-07-25

- **Task / Issue**: Close the final PR #660 integration residual where template batch spawn could drain multiple requests before verifying receipt/on-spawn success-signal capacity for the whole batch.
- **Date**: 2026-07-25
- **Agent / Author**: Codex.

### 1. Core judgment

Primary delivery: A. Tighten the existing `RuntimeEntitySpawnQueue` / `RuntimeEntitySpawnSystem` batch transaction boundary.

Result: PASS.

Reason: The repair adds a non-mutating queue peek helper and moves batch success-signal preflight before dequeue. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel spawn pipeline.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Non-mutating spawn queue batch inspection | 1 | `RuntimeEntitySpawnQueue.TryPeekAt` |
| Template batch receipt/on-spawn preflight before dequeue | 1 | `RuntimeEntitySpawnSystem.PreflightTemplateBatchBeforeDrain` |
| Regression coverage for retryable failed batch spawn | N/A | `RuntimeEntitySpawnSystem_BatchTemplateReceiptCapacity_DoesNotDrainRequests` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: existing `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`, `RuntimeEntitySpawnReceiptQueue`, `EffectRequestQueue`.
- Resolvers / Registries: existing `EntityTemplateKeyRegistry`, template registry, performer bootstrap checks.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Template batch spawn now copies the contiguous batch by peek, preflights relationships, receipt capacity, presentation event capacity, and on-spawn effect queue requirements before removing any request from `RuntimeEntitySpawnQueue`. If capacity is insufficient, all spawn requests remain retryable and no entity is materialized.

### 6. Config SSOT

Behavior remains in existing entity templates, runtime spawn requests, receipt queue capacity, and effect request queue capacity.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn/effect runtime added
- [x] No fallback or silent request drain added
- [x] No new registry, loader, preset, or schema added

### 8. Next variant test

A new template spawn variant changes template data or request fields and continues through the same preflight-before-dequeue batch path. It does not add an alternate spawn pipeline.

## PR #660 Final Cursor Repair Lanes - 2026-07-24

- **Task / Issue**: Final PR #660 repair pass for RoadNetwork order admission, core order lifecycle ownership, ResponseChain prompt transactions, and side-effect commit ordering.
- **Date**: 2026-07-24
- **Agent / Author**: Codex supervising Cursor Agent lanes.

### 1. Core judgment

Primary delivery: A. Tighten existing order/admission/lifecycle/effect code paths using existing queues, registries, payload ownership helpers, and terminal-result buffers.

Result: PASS.

Reason: The repair reuses `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderSubmitter`, `OrderBufferSystem`, `OrderContinuationSystem`, `OrderSpatialPayloadOps`, `InputRequestQueue`, `OrderRequestQueue`, `EffectRequestQueue`, `RuntimeEntitySpawnReceiptQueue`, `AbilityExecSystem`, `EffectProposalProcessingSystem`, `RuntimeEntitySpawnSystem`, and RoadNetwork showcase order plumbing. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel order/runtime pipeline.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| RoadNetwork typed single submit result | 1 | `RoadMoveOrderExpander.TrySubmit` returns `OrderSubmitResult` |
| RoadNetwork mid-batch payload rollback | 1 | `OrderSpatialPayloadOps.Release` on rejected route batch |
| GlobalIntake cross-frame visibility | 1 | `OrderAdmissionResultBuffer` carry-forward |
| Batch preview / submit alignment | 1 | `OrderSubmitter.Preview` blackboard preparation checks |
| Queue cleanup terminal outcomes | 1 | `OrderSubmitter` release/cancel helpers publish terminal results |
| Queued promotion typed failure | 1 | `TryPromoteNextQueuedToActive` failure output plus EntityIntake admission |
| Continuation/incoming ownership | 1 | `OrderContinuationSystem` restore path and `OrderBufferSystem` process-before-dequeue |
| Response prompt visible transaction | 1 | `EffectProposalProcessingSystem` preflights InputRequest + OrderRequest before either is visible |
| Ability terminal vs toggle/effect commit | 1 | `AbilityExecSystem` preflights/commits side effects before terminal success |
| Spawn receipt/effect success signal | 1 | `RuntimeEntitySpawnSystem` preflights success signals before create/dequeue |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderContinuationSystem`, `OrderAdmissionResultBuffer`, `OrderTerminalResultBuffer`, `InputRequestQueue`, `OrderRequestQueue`, `EffectRequestQueue`, `RuntimeEntitySpawnReceiptQueue`, RoadNetwork local order source.
- Resolvers / Registries: `OrderTypeRegistry`, `OrderRuleRegistry`, `OrderSubmitter`, `OrderSubmitResultSemantics`, `OrderSpatialPayloadOps`, `RoadRoutePlanningService`, `AbilityDefinitionRegistry`, `EffectTemplateIdRegistry`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

RoadNetwork shared-batch planning is all-or-nothing for built route payloads. Shared/clustered order preview must reject known blackboard preparation failures before mutating any actor. Queue cleanup and promotion failure must leave explicit terminal/admission results. Continuation and incoming paths must not lose order ownership when expected submit-preparation failures occur. ResponseChain WaitInput publishes InputRequest and OrderRequest as one visible transaction. Ability and spawn success signals must not be published before required side effects, receipts, or on-spawn effect requests can succeed.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, RoadNetwork route planning services, ability toggle specs, entity template onSpawnEffect data, and runtime capacity values.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel order, spawn, effect, or lifecycle runtime added
- [x] No placement validation moved into lifecycle operations
- [x] No fallback or queue-full reason compression added

### 8. Next variant test

A new command, RoadNetwork, ability, or spawn variant changes input mapping, effect/graph wiring, route planning data, or template data and continues through the same admission, payload ownership, side-effect, and terminal-result contract.

## PR #660 Order Admission Follow-up Repair - 2026-07-24

This is the current live self-review section for the final order admission follow-up on branch `codex/issues-649-651-ordering`. Older PR #660 sections below are historical context, not alternate active branches.

- **Task / Issue**: Close the remaining #650 / #651 batch result contract gaps after the `62a2a928...` repair.
- **Date**: 2026-07-24
- **Agent / Author**: Codex.

### 1. Core judgment

Primary delivery: A. Tighten the existing Order admission and input fan-out contract so every assigned batch order id has a queryable typed result, and batch submitters can preserve their real rejection reason.

Result: PASS.

Reason: This work reuses the existing `OrderQueue`, `OrderAdmissionResultBuffer`, `InputOrderMappingSystem`, `CompositeOrderPlanner`, CoreInput local order source, RoadNetwork order expander, and launcher evidence recorder. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Batch admission-result capacity failure stays queryable for every assigned row | N/A | existing `OrderAdmissionResultBuffer` rejection area |
| Batch queue APIs return typed submit results | N/A | existing `OrderQueue` batch/shared/clustered entrypoints |
| Collection fan-out preserves handler rejection reason | N/A | existing `InputOrderMappingSystem` batch submission path |
| Mod and tool batch callers consume typed outcomes | N/A | existing CoreInput, RoadNetwork, and launcher evidence call sites |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderAdmissionResultBuffer`, `InputOrderMappingSystem`, `CompositeOrderPlanner`.
- Resolvers / Registries: existing order type ids, input mapping data, local order source hooks.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch intake assigns ids first, then checks whether normal admission-result slots can hold the whole batch. If normal slots are unavailable but rejection slots can hold the batch, the queue is not mutated and every assigned id gets `RejectedAdmissionCapacity`. If a batch handler rejects for rules, validation, or queue capacity, the input activation result returns that exact `OrderSubmitResult` instead of translating all failures to queue-full.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, response-chain queue contracts, and runtime capacity values.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel order, response-chain, continuation, or lifecycle runtime added
- [x] No fallback constructor or compatibility bypass added
- [x] No silent zero-id, swallowed batch rejection, or unqueryable assigned order result remains in the repaired batch paths
- [x] No Core gameplay enum, preset, loader, registry, or schema added

### 8. Verification

- `dotnet build src\Tests\GasTests\GasTests.csproj -c Debug --no-restore --nologo -v:minimal`: PASS.
- `dotnet test src\Tests\GasTests\GasTests.csproj --no-build -c Debug --filter "FullyQualifiedName~InputOrderAbilityAuditTests|FullyQualifiedName~MovePlanOrderLifecycleTests|FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~InteractionSelectionConvergenceTests" --logger "console;verbosity=minimal" -m:1`: PASS, 159/159.
- `dotnet test src\Tests\GasTests\GasTests.csproj --no-build -c Debug --filter "FullyQualifiedName~InputOrderAbilityAuditTests|FullyQualifiedName~MovePlanOrderLifecycleTests|FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~ResponseChainPresenterPipelineTests|FullyQualifiedName~OrderCompositePlannerTests" --logger "console;verbosity=minimal" -m:1`: PASS, 149/149.
- `dotnet test src\Tests\GasTests\GasTests.csproj --no-build -c Debug --filter "TestCategory=ci-gate" --logger "console;verbosity=minimal" -m:1`: PASS, 166/166.
- `dotnet build src\Tools\Ludots.Launcher.Evidence\Ludots.Launcher.Evidence.csproj -c Debug --nologo -v:minimal`: PASS.

### 9. Next variant test

A new player command variant changes input mapping data, effect chains, or graph wiring and continues through the same order admission/result contract. It must not add an alternate Core order runtime or silently bypass admission results.

## PR #660 Order Admission Mergeability Repair - 2026-07-24

This is the live self-review section for the order admission repair on branch `codex/issues-649-651-ordering`. Older sections below are historical context only.

- **Task / Issue**: PR #660 strict audit repair for #649 / #650 / #651 / #669 order-result traceability.
- **Date**: 2026-07-24
- **Agent / Author**: Codex.

### 1. Core judgment

Primary delivery: A. Repair existing Order admission, input fan-out, ResponseChain, and continuation paths so every player-visible order attempt produces a typed, queryable result.

Result: PASS.

Reason: This work reuses the existing `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `InputOrderMappingSystem`, `ResponseChain*OrderSourceSystem`, `CompositeOrderPlanner`, and `OrderContinuationSystem`. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Batch queue-full rejection keeps nonzero order ids | N/A | existing `OrderQueue` + `OrderAdmissionResultBuffer` |
| Collection fan-out returns assigned batch id or typed rejection | N/A | existing `InputOrderMappingSystem` batch submit path |
| ResponseChain enqueue failure is observable and retryable | N/A | existing `ResponseChainHumanOrderSourceSystem` and `ResponseChainAiOrderSourceSystem` |
| Continuation follow-up admission result is published | N/A | existing `OrderContinuationBuffer`, `OrderContinuationSystem`, `OrderSubmitter` |
| Entity-intake close remains explicit in production | N/A | existing cleanup phase with `OrderAdmissionEntityIntakeEndSystem` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `OrderContinuationSystem`, `ResponseChainHumanOrderSourceSystem`, `ResponseChainAiOrderSourceSystem`.
- Resolvers / Registries: existing `OrderTypeRegistry`, input mapping profiles, response-chain order type ids.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch and continuation intake must reserve admission-result capacity before mutating queue or continuation state. Queue-full paths must publish `RejectedQueueFull` with a nonzero order id instead of silently dropping, throwing from player input, or marking an AI response as submitted when it was not accepted.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, response-chain queue contracts, and runtime capacity values.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel order, response-chain, continuation, or lifecycle runtime added
- [x] No fallback constructor or compatibility bypass added
- [x] No silent queue-full, zero-id rejection, or unqueryable order result remains in the repaired paths
- [x] No Core gameplay enum, preset, loader, registry, or schema added

### 8. Next variant test

A new player command variant changes input mapping data, effect chains, or graph wiring and continues through the same order admission/result contract. It must not add an alternate Core order runtime or silently bypass admission results.

## PR #660 Final Status - 2026-07-24

This is the current status for the PR #660 worktree. Older sections below are historical evidence and must not be treated as alternate live branches.

- Worktree: `C:\001_AI\_codex_audit\Ludots-pr660-086d3f4-exact-20260724-1415`.
- Branch: `codex/issues-649-651-ordering`.
- Starting PR head before this repair: `086d3f4f35079eaab417bfea166335e5ba9c9b9d`.
- Main merge base used here: `5712a4eef4cdb1011cc0694d52e77de95bfe4aaa`.
- Related issues #649 / #650 / #651 remain the original order/input work history for this PR. They are not separate active branches; current acceptance status is this section.

Current blockers repaired:

- Grid/NodeGraph map assets now declare positive `LoadedChunkCapacity`; legacy `WidthInTiles` / `HeightInTiles` map fields are rejected by regression coverage.
- Ability input and target-collection gates fail the active order when the input request queue is missing/full instead of entering a permanent fake wait.
- Response-chain window depth overflow, response queue overflow, prompt input queue failure, and prompt order request queue failure are explicit failures instead of silent drops.
- Graph blackboard writes fail on dead entities or missing pre-added blackboard buffers instead of silently returning.

Verification on this worktree:

- `dotnet test src\Tests\GasTests\GasTests.csproj --filter "FullyQualifiedName~ResponseWindowRobustnessTests|FullyQualifiedName~GraphFailFastAndCapacityTests|FullyQualifiedName~EffectPhaseArchitectureTests|FullyQualifiedName~InteractionSelectionConvergenceTests" -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 80/80.
- `dotnet test src\Tests\GasTests\GasTests.csproj --filter FullyQualifiedName~MapAssets_GridAndNodeGraphBoards_DeclarePositiveLoadedChunkCapacity -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 1/1.
- `dotnet test src\Tests\PresentationTests\PresentationTests.csproj --filter FullyQualifiedName~Showcase_UnchangedRelationshipRevisionDoesNotRepeat10kDomainResolution -c Debug --no-restore --logger "console;verbosity=minimal" -clp:ErrorsOnly -m:1`: PASS, 1/1.
- `dotnet build` for every `src\Tests\**\*.csproj`, then `dotnet build src\Tools\ModdingSmoke\ModdingTest.csproj -c Debug`: PASS.
- `solution-verify.yml` architecture guard slice: PASS, 80/80.
- `solution-verify.yml` MassNavigation PR acceptance slice: PASS, 11/11.
- `dotnet test src\Tests\GasTests\GasTests.csproj -c Debug --no-build --filter "TestCategory=ci-gate" --logger "console;verbosity=minimal"`: PASS, 166/166 after an initial local Fog benchmark throughput-noise rerun.
- `dotnet test src\Tests\RaylibAdapterTests\RaylibAdapterTests.csproj -c Debug --no-build --filter "TestCategory=raylib-field" --logger "console;verbosity=minimal"`: PASS, 4/4.
- `git diff --check`: PASS.
- `rg -n "WidthInTiles|HeightInTiles" mods assets src -g "*.json"`: no matches.
- CI audit gate artifacts: `artifacts/ci-audit/pr660/result.md` and `artifacts/ci-audit/pr660/result.json`.

## GAS Composition Gate - PR #660 Final Mergeability Repair

- **Task / Issue**: PR #660 final mergeability repair after strict audit found CI map-load failure and remaining silent-drop paths
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A

缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 鏈疆淇娌跨幇鏈?BoardConfig/Map merge銆丟AS queue銆丷esponseChain銆丒ffect side-effect transaction銆丟raph blackboard 鍐欏叆绠＄嚎琛ラ綈澶辫触璇箟锛涗笉鏂板 profile enum銆乸reset 寮€鍏炽€乴oader銆乺egistry銆乸arallel runtime 鎴?fallback銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| 鍦板浘 loaded chunk 瀹归噺濂戠害 | N/A | existing `BoardConfig`, map JSON assets, `BoardFactory`/board constructors |
| Ability input gate enqueue failure | N/A | existing `AbilityExecSystem`, `InputRequestQueue` |
| ResponseChain create/depth/queue overflow | Layer 2 execution repair | existing `EffectProposalProcessingSystem`, `EffectProposalWindow`, `ProposalResponseQueue` |
| Response prompt order request enqueue failure | N/A | existing `OrderRequestQueue` |
| Blackboard write dead/missing component failure | Layer 2/graph runtime guard | existing `GasGraphRuntimeApi`, `EffectPhaseSideEffectTransaction` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `InputRequestQueue`, `OrderRequestQueue`, `EffectProposalProcessingSystem`, `AbilityExecSystem`.
- Resolvers / Registries: `BoardConfig`, `MapManager`, existing graph blackboard buffers and `EffectPhaseSideEffectTransaction`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops (if any)

N/A.

### 5. Transaction boundary

ResponseChain overflow and prompt enqueue failures must stop the active response window instead of continuing with missing responses/prompts. Blackboard side effects staged through `EffectPhaseSideEffectTransaction` must reject dead or missing target storage before commit-visible state can pretend success.

### 6. Config SSOT

琛屼负閰嶇疆钀藉湪: existing map board JSON (`LoadedChunkCapacity`) and existing GAS graph/effect runtime contracts.

鏄惁鏂板 JSON schema: NO.

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn 骞宠鐨勭墿鍖栫绾?- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犮€岃涓嶆竻鐨勩€嶉粯璁?fallback

### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: graph 杩炵嚎 / effect 姝ラ / map runtime capacity data.

---

## GAS Composition Gate - PR #660 Capacity Fail-Fast Repair

- **Task / Issue**: PR #660 merge readiness repair for GAS capacity overflow audit findings
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Repair existing GAS queue, fan-out, root-budget, active-effect, and phase-listener capacity handling.

Result: PASS

Reason: This work changes existing budget enforcement from silent drop/truncation to explicit capacity failure. It adds no graph op, effect preset enum, gameplay profile field, loader, registry, fallback path, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Root budget table full-table guard | N/A | existing `RootBudgetTable` |
| Fan-out root budget exhaustion | N/A | existing `TargetResolverFanOutHelper` |
| Effect request queue overflow | N/A | existing `EffectRequestQueue` |
| Gameplay event bus overflow | N/A | existing `GameplayEventBus` |
| Phase listener dispatch capacity | N/A | existing `EffectPhaseExecutor` scratch buffer |
| Active effect/listener registration capacity | N/A | existing `EffectApplicationSystem` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `EffectRequestQueue`, `GameplayEventBus`, `EffectProcessingLoopSystem`, `EffectProposalProcessingSystem`, `EffectApplicationSystem`, `EffectLifetimeSystem`.
- Resolvers / Registries: `TargetResolverFanOutHelper`, `RootBudgetTable`, `GlobalPhaseListenerRegistry`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Capacity overflow now fails before publishing partial requests, dropping fan-out targets, truncating listener dispatch, destroying an unattached active effect, or partially registering listener entries. Existing side-effect transactions continue to own rollback for graph-triggered side effects.

### 6. Config SSOT

Root fan-out budget capacity is derived from the existing `gasRuntimeCapacity.effectFanOutCommandCapacity` path through the existing constructors. New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel queue, event bus, effect pipeline, or listener runtime added
- [x] No fallback constructor or silent overflow branch added
- [x] No capacity truncation that lets gameplay continue with missing effects/events
- [x] No Core gameplay enum, preset type, loader, or registry added

### 8. Next variant test

The next Mod variant changes effect chains, graph wiring, or configured runtime capacity. It must not change Core enums to bypass a capacity failure.

---

## GAS Composition Gate - PR #660 Final Main-Lag Repair

- **Task / Issue**: PR #660 final merge repair against current `main`
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A锛堟部鐜版湁 Order/GAS銆並nowledge銆乁I Surface銆両nput 涓?Physics2D 绠＄嚎淇鍚堝苟钀藉悗闂锛?
缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 鏈疆鍙慨姝ｇ幇鏈?showcase銆侀獙鏀跺涓诲拰鐑矾寰勪娇鐢ㄦ柟寮忥紱娌℃湁鏂板 graph op銆乪ffect preset enum銆乸rofile 瀛楁銆乴oader銆乺egistry銆乫allback 鎴栧钩琛岃繍琛屾椂銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| Interaction stress order pressure | N/A | existing `gasRuntimeCapacity` budget and existing stress fireball effect templates |
| Relationship frontend mount | N/A | existing `AcceptanceUiHostInstaller` + `UiSurfaceHost` |
| Physics2D steady-state allocation repair | N/A | existing `InputActionAttributeBindingSystem` and `Physics2DSimulationSystem` inline queries |
| Performance Visualization Health HUD audience | N/A | existing `LocalPlayerEntity`, `KnowledgeProjectionStore`, `KnowledgeDisclosureRecord` |
| UX Prototype selectable visibility | N/A | existing local player services, `PlayerEntityLookup`, `KnowledgeProjectionStore` |

### 3. Reuse list

- Handlers: existing GAS effect handlers; no new builtin handler.
- Queues / Systems: `OrderAdmissionResultBuffer`, `RuntimeEntitySpawnQueue`, `InputActionAttributeBindingSystem`, `Physics2DSimulationSystem`, existing presentation/UI runtime.
- Resolvers / Registries: `KnowledgeProjectionStore`, `PlayerEntityLookup`, `EntityTemplateKeyRegistry`, existing UI surface services.
- Existing presets / graphs: unchanged; stress fireball keeps existing `LaunchProjectile` / `InstantDamage` presets and only opts out of response-chain participation for the stress-only path.

### 4. New Layer 0 ops (if any)

N/A. No atomic op, graph operation, effect handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

蹇呴』鍘熷瓙 rollback 鐨勬楠? N/A锛涙湰杞笉鏀?mutation transaction銆侽rder 鍘嬪姏淇鍙榻愮幇鏈夊叆闃?鎺ュ崟缁撴灉瀹归噺锛沀I 淇鍙ˉ榻愰獙鏀?composition root锛汸hysics2D 淇鍙紦瀛樼ǔ瀹氱浉鏈鸿緭鍏ユ壙杞藉疄浣撳苟浣跨敤 inline-query 璇诲彇缁熻銆?
### 6. Config SSOT

琛屼负閰嶇疆钀藉湪:

- `mods/showcases/interaction/InteractionShowcaseMod/assets/game.json`锛歴howcase 涓撳睘 Order admission / rejection capacity銆?- `mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/effects.json`锛歴tress-only fireball response-chain participation銆?- 鍏朵粬淇澶嶇敤鐜版湁 runtime services锛屼笉鏂板閰嶇疆鏂囦欢銆?
鏄惁鏂板 JSON schema: NO

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn/effect/order/UI/physics 骞宠鐨勮繍琛屾椂绠＄嚎
- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犻粯璁?fallback銆佸吋瀹规梺璺垨闈欓粯瀹归噺鏀捐繃
- [x] 鏈柊澧?registry銆乸reset銆乴oader 鎴?schema
- [x] 鐑矾寰勪慨澶嶄笉鏂板 ECS 缁撴瀯鍙樻洿鎴栨墭绠￠泦鍚堝闀?
### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: graph 杩炵嚎 / effect 姝ラ / showcase 鏁版嵁棰勭畻銆傝緭鍏ャ€佺煡璇嗗彲瑙佹€с€乁I 鎸傝浇鍜?Physics2D 缁熻缁х画璧扮幇鏈夋湇鍔′笌绯荤粺锛屼笉淇敼 Core gameplay enum銆?
---

## GAS Composition Gate - PR #660 Spawn Relationship Test Repair

- **Task / Issue**: PR #660 merge repair for runtime spawn Team 鈫?MemberOf relationship contract
- **Date**: 2026-07-23
- **Agent / Author**: Codex

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A锛堟祴璇曡閰嶅鐢ㄧ幇鏈?spawn 涓?relationship 鍩哄缓锛?
缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 鏈疆鍙 runtime spawn 娴嬭瘯鏄惧紡娉ㄥ叆姝ｅ紡 `RelationshipRuntime`銆乣RelationshipTypeRegistry` 涓?`TeamEntityLookup`锛屼笉鏂板 graph op銆乸reset銆乸rofile 瀛楁銆乴oader銆乺egistry 鎴栧钩琛岀墿鍖栫绾裤€?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| Team 浠ｈ〃瀹炰綋瑁呴厤 | N/A | `TeamIdentity` + `TeamEntityLookup` |
| MemberOf 绫诲瀷娉ㄥ唽 | N/A | `RelationshipTypeRegistry` |
| Spawn 鍚庨槦浼嶅綊灞炲叧绯?| N/A | existing `RuntimeEntitySpawnSystem` + `RelationshipRuntime` |

### 3. Reuse list

- Handlers: existing `CreateUnit` / runtime spawn path unchanged
- Queues / Systems: existing `RuntimeEntitySpawnQueue` and `RuntimeEntitySpawnSystem`
- Resolvers / Registries: `RelationshipTypeRegistry`, `TeamEntityLookup`, `RelationshipReverseIndex`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

蹇呴』鍘熷瓙 rollback 鐨勬楠? N/A锛涙湰杞笉鏀圭敓浜т簨鍔★紝鍙ˉ娴嬭瘯 composition root銆俿pawn 绯荤粺缁х画鍦ㄥ叆闃熸秷璐瑰墠鏄惧紡鏍￠獙 Team 浠ｈ〃鍜?MemberOf 绫诲瀷锛岀己澶卞嵆 hard failure銆?
### 6. Config SSOT

琛屼负閰嶇疆钀藉湪: existing spawn request and relationship runtime registration.

鏄惁鏂板 JSON schema: NO

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn 骞宠鐨勭墿鍖栫绾?- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犻粯璁?fallback 鎴栧吋瀹规梺璺?- [x] 鏈柊澧?registry銆乸reset 鎴?schema

### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: effect 姝ラ / graph 杩炵嚎銆傞渶瑕侀槦浼嶅綊灞炴椂缁х画閫氳繃姝ｅ紡 relationship runtime 鍜?Team 浠ｈ〃瀹炰綋琛ㄨ揪銆?
---

## GAS Composition Gate - PR #660 main merge repair

- **Task / Issue**: PR #660 merge with current `main`
- **Date**: 2026-07-23
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Merge the existing typed Order admission/result contract with the existing mainline batch, shared-id, and clustered command admission path.

Result: PASS

Reason: The repair reuses `OrderQueue`, `OrderAdmissionResultBuffer`, `OrderBufferSystem`, `OrderSubmitter`, `InputOrderMappingSystem`, and existing MovePlan/MassNavigation ports. It adds no gameplay profile enum, effect preset switch, graph op, lifecycle DSL, loader, or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Typed global/entity order admission | N/A | `OrderAdmissionResultBuffer` |
| Atomic batch/shared/clustered intake | N/A | `OrderQueue` |
| Batch preflight before activation | N/A | `OrderBufferSystem` |
| Actor authorization before fan-out submit | N/A | `InputOrderMappingSystem` |
| MovePlan-backed road execution binding | N/A | existing `MassNavigationRuntimeBinding` and `MovePlanStore` |

### 3. Reuse list

- Handlers: no new BuiltinHandler.
- Queues / Systems: `OrderQueue`, `OrderBufferSystem`, `OrderSubmitter`, existing `SystemGroup.RuntimeEntityBinding`.
- Resolvers / Registries: `OrderTypeRegistry`, `CommandIntentProfileRegistry`, `CastDispatchProfileRegistry`, `MassNavigationRuntimeBinding`.
- Existing presets / graphs: unchanged.

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Batch intake validates order type, capacity, actor ownership, command-source grouping, and entity intake preflight before any row activates. If one batch member fails entity intake, every row is rejected and spatial payload ownership is released.

### 6. Config SSOT

Behavior remains in the existing order type catalog, input mapping data, and GAS runtime capacity config. New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order, MovePlan, or lifecycle runtime added
- [x] No fallback constructor restored for `OrderQueue`
- [x] No silent partial batch admission

### 8. Next variant test

The next Mod variant changes command routing data or effect/graph composition, not Core enums.

---

## GAS Composition Gate - #649 Production Closeout

- **Task / Issue**: #649
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Extend the existing Order pipeline with one typed, bounded terminal-result contract.

Result: PASS

Reason: The change reuses OrderSubmitter, OrderTypeRegistry, OrderContinuationBuffer and the SchemaUpdate frame boundary. It adds no gameplay profile, preset, graph op, loader or parallel order runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Unique order finalization | N/A | `OrderSubmitter.FinalizeActive` |
| Current-frame terminal snapshot | N/A | `OrderTerminalResultBuffer` owned by `OrderTypeRegistry` |
| Continuation dispatch | N/A | `OrderContinuationSystem` consumes every typed outcome once |
| Frame reset | N/A | Existing `GasBudgetResetSystem` in SchemaUpdate |

### 3. Reuse list

- Handlers: existing OrderSubmitter activation and blackboard cleanup helpers
- Queues / Systems: OrderBuffer, OrderContinuationBuffer, OrderContinuationSystem, GasBudgetResetSystem
- Resolvers / Registries: OrderTypeRegistry
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A.

### 5. Transaction boundary

Terminal-result capacity and outcome validity are checked before the active order is mutated. The single internal finalizer then clears blackboard and active state, removes non-completed continuations, publishes exactly one typed outcome, and optionally promotes the next queued order.

### 6. Config SSOT

Production capacity is explicitly configured at `game.json -> gasRuntimeCapacity.orderTerminalResultCapacity` and validated as positive during engine composition.

New JSON schema: YES. This is a runtime capacity budget, not gameplay behavior and cannot be represented by effect/graph composition.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order or terminal runtime added
- [x] No placement validation moved into lifecycle operations
- [x] No default gameplay fallback or silent capacity drop added
- [x] No per-finalization ECS component add/remove; continuation consumes the bounded result snapshot directly

### 8. Next variant test

Future order variants publish through the same finalizer and terminal-result buffer. They do not add Core enums, consumer-specific signals or alternate completion paths.

---

## GAS Composition Gate - #647 Consumer Closeout

- **Task / Issue**: #647
- **Date**: 2026-07-12
- **Result**: PASS
- CoreInput aiming, context-scored order routing, and the entity-query showcase now consume the engine-owned `GasGraphRuntimeApi` service.
- Consumer-side `CreateProduction` calls were removed; missing composition-root service is an explicit hard failure.
- No graph op, profile schema, preset enum, loader, or parallel runtime was added.

---

## GAS Composition Gate - Combined Order/Input Review

- **Task / Issue**: #650, #649, #651
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A锛堟部鐜版湁 Order/Input 绠＄嚎鎵╁睍绫诲瀷鍖栫粨鏋滃悎鍚岋紱涓嶆柊澧?gameplay 鍙樹綋锛?
缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 淇敼闄愪簬鐜版湁璁㈠崟鎺ュ叆銆佽鍗曠粓鎬佸拰杈撳叆婵€娲诲叆鍙ｏ紝涓嶆柊澧?profile銆乸reset銆乬raph銆乴ifecycle op 鎴栧钩琛岀绾裤€?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| 璁㈠崟鎺ュ叆缁撴灉 | N/A | 鐜版湁 OrderQueue銆丱rderSubmitter銆丱rderBufferSystem |
| 璁㈠崟鍞竴缁堟€?| N/A | 鐜版湁 OrderSubmitter銆丄bilityExecSystem銆丱rderContinuationSystem |
| 瑙掕壊闅旂婵€娲?| N/A | 鐜版湁 InputOrderMappingSystem銆丒ntityCommandPanelMod |

### 3. Reuse list

- Handlers: 鐜版湁 InputOrderMappingSystem.OrderSubmitHandler
- Queues / Systems: OrderQueue銆丱rderBufferSystem銆丄bilityExecSystem銆丱rderContinuationSystem
- Resolvers / Registries: OrderTypeRegistry銆丄bilityDefinitionRegistry銆佺幇鏈?actor/mapping 瑙ｆ瀽
- Existing presets / graphs: N/A

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

蹇呴』鍘熷瓙 rollback 鐨勬楠? N/A锛涜鍗?finalize 閫氳繃鍗曚竴鍏ュ彛淇濊瘉姣忎釜 active order 鍙粨鏉熶竴娆°€?
### 6. Config SSOT

琛屼负閰嶇疆钀藉湪: 鐜版湁 order type catalog 涓?OrderBuffer 姝ｅ紡瀹归噺銆?
鏄惁鏂板 JSON schema: NO

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn 骞宠鐨勭墿鍖栫绾?- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犮€岃涓嶆竻鐨勩€嶉粯璁?fallback

### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: effect 姝ラ锛堟湰浠诲姟鏈韩涓嶅紩鍏?Mod gameplay 鍙樹綋锛?
---

## GAS Composition Gate 鈥?#646 Self Review

- **Task / Issue**: #646
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

涓昏浜や粯鐗╀负 A锛氭部鐜版湁 Effect Phase銆丅uiltinHandler銆丒ffectRequestQueue 涓庢椂闂村垏鐗囧悎鍚岄噸缁勬墽琛岃矾寰勶紝涓嶆柊澧?profile 瀛楁銆乸reset 鏋氫妇銆丟raph VM 鎴栧钩琛岀绾裤€?
缁撹锛歅ASS銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer | 瀹炵幇杞戒綋 |
|---|---:|---|
| 鐬椂鏁堟灉闃舵缁勫悎 | 2 | 鐜版湁 `EffectPhaseExecutor` + effect template phase bindings |
| 棰勮涓昏涓?| 0 | 鐜版湁 `BuiltinHandlerRegistry` / `BuiltinHandlers` |
| 鐢熷懡鍛ㄦ湡鍒囩墖 | N/A | 鐜版湁 `EffectLifetimeSystem` + `ITimeSlicedSystem` |
| 鍚庣画鏁堟灉鍙戝竷 | N/A | 鐜版湁 `EffectRequestQueue` |

### 3. Reuse list

- Handlers: `BuiltinHandlerRegistry`, `BuiltinHandlers`, `BuiltinHandlerExecutionContext`
- Queues / Systems: `EffectRequestQueue`, `EffectProcessingLoopSystem`, `EffectLifetimeSystem`, `AbilityExecSystem`
- Resolvers / Registries: `EffectPhaseExecutor`, `EffectTemplateRegistry`, existing target resolvers and graph program registry
- Existing presets / graphs: existing `EffectPresetType` definitions and phase graph bindings; no new variant

### 4. New Layer 0 ops

N/A銆?
### 5. Transaction boundary

鐬椂鏁堟灉蹇呴』鍦ㄥ悓涓€娆℃寮忛樁娈垫墽琛屼腑瀹屾垚 OnResolve銆丱nHit銆丱nApply锛屽苟鍦ㄧ粨鏉熸椂娓呯悊閰嶇疆涓婁笅鏂囧拰鎵囧嚭鏆傚瓨锛涢渶瑕佽法甯х洃鍚櫒鎵€鏈夋潈鐨勬晥鏋滀笉寰楄繘鍏ョ灛鏃惰矾寰勩€?
### 6. Config SSOT

琛屼负浠嶇敱鐜版湁 effect template銆乸reset catalog 鍜?graph 閰嶇疆琛ㄨ揪銆傛柊澧炵殑浠呮槸 `game.json` 涓惎鍔ㄦ湡 GAS 蹇収瀹归噺锛涙病鏈夋柊澧炵帺娉?DSL銆?
### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤哄钩琛岀灛鏃惰繍琛屾椂銆丟raph VM 鎴?loader
- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 瀹归噺涓嶈冻鏄庣‘澶辫触锛岄绠椾笉瓒虫槑纭欢鍚庯紝鏃?fallback/闈欓粯鎴柇

### 8. Next variant test

涓嬩竴涓?Mod 鍙樹綋鍙皟鏁?graph 杩炵嚎鎴?effect 姝ラ锛屼笉淇敼 Core enum銆?
---

## GAS Composition Gate 鈥?#647 Self Review

- **Task / Issue**: #647
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

涓昏浜や粯鐗╀负 A锛氱粺涓€鐜版湁鐢熶骇 Graph API 鐨勬湇鍔¤閰嶅拰鐢熷懡鍛ㄦ湡/璇婃柇鎺ョ嚎锛涙病鏈夋柊澧?graph op銆乸reset 瀛楁銆佺帺娉曟灇涓炬垨骞宠杩愯鏃躲€?
缁撹锛歅ASS銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer | 瀹炵幇杞戒綋 |
|---|---:|---|
| 鐢熶骇鍥炬湇鍔¤閰?| N/A | `GasGraphRuntimeApi.CreateProduction` + `GasGraphRuntimeProductionServices` |
| 娲剧敓灞炴€у浘鎵ц | 2 | 鐜版湁 `GraphProgramRegistry` + `AttributeAggregatorSystem` |
| 杈撳嚭鐢熷懡鍛ㄦ湡 | N/A | 鐜版湁 `GraphOutputValueStore` + Cleanup system |
| GAS 鍛婅鍑哄彛 | N/A | 鐜版湁 `GasBudget` / `OrderAdmissionResultBuffer` + 鍥哄畾瀹归噺缁撴瀯鍖栦簨浠剁紦鍐?|

### 3. Reuse list

- Handlers: existing `GasGraphOpHandlerTable`
- Queues / Systems: `AttributeAggregatorSystem`, `GasBudgetReportSystem`, `OrderAdmissionResultBuffer`
- Resolvers / Registries: `GraphProgramRegistry`, topology services, `GraphOutputValueStore`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A銆?
### 5. Transaction boundary

鐢熶骇 Graph API 鏋勯€犺姹傚畬鏁存湇鍔￠泦鍚堬紝缂哄け浠讳竴姝ｅ紡渚濊禆绔嬪嵆澶辫触锛沷wner 鐗堟湰閫€褰规椂锛岃緭鍑烘Ы浣嶃€佸搱甯岀储寮曞拰鏃у彞鏌勫湪鍚屼竴 Cleanup 鏇存柊涓竴璧峰け鏁堛€?
### 6. Config SSOT

娌℃湁鏂板鐜╂硶閰嶇疆 schema銆傜敓浜ф湇鍔℃潵鑷紩鎿庡敮涓€寮虹被鍨嬫湇鍔￠泦鍚堬紱璇婃柇鎸囨爣鏉ヨ嚜 `GasBudget` 涓庤鍗曟帴鍏ョ粨鏋溿€?
### 7. Red flag scan

- [x] 鏈柊澧?profile enum 鎴?graph op
- [x] 鏈缓绔嬬浜屽 Graph API 杩愯鏃?- [x] 鏈敤鍏?ECS 琛ㄦ壂鎻忔垨瀹氭湡鍏ㄦ竻瀹炵幇杈撳嚭鍥炴敹
- [x] 缂烘湇鍔°€佽瘖鏂紦鍐叉孩鍑哄拰璁℃暟鍣ㄥ洖閫€鍧?hard-stop

### 8. Next variant test

涓嬩竴涓?Mod 鍙樹綋缁х画閫氳繃鐜版湁 graph 杩炵嚎鍜屾湇鍔￠泦鍚堟帴鍏ワ紝涓嶄慨鏀?Core enum銆?
---

## GAS Composition Gate 鈥?#653 Self Review

- **Task / Issue**: #653
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

涓昏浜や粯鐗╀负 A锛氭敹鍙ｇ幇鏈夋湁鏁堟妧鑳芥Ы浣嶈В鏋愪笌杈撳叆/灞曠ず鎺ョ嚎锛涙病鏈夋柊澧?ability profile銆乸reset銆乬raph op 鎴栫浜屽浼樺厛绾ц鍒欍€?
缁撹锛歅ASS銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer | 瀹炵幇杞戒綋 |
|---|---:|---|
| 鏈夋晥鎶€鑳芥Ы浣嶈В鏋?| N/A | 鍞竴 `AbilitySlotResolver.Resolve` |
| 杈撳叆瑕嗙洊 | N/A | `SkillMappingOverrideResolver` |
| 闈㈡澘涓庡疄浣撲俊鎭睍绀?| N/A | 鐜版湁 EntityCommandPanel / EntityInfo 娑堣垂鑰?|

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing input mapping, routing, aiming and execution systems
- Resolvers / Registries: `AbilitySlotResolver`, `AbilityDefinitionRegistry`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A銆?
### 5. Transaction boundary

鍚屼竴 actor/slot 鐨?base銆乫orm銆乮tem銆乬ranted 鏉ユ簮蹇呴』涓€璧峰弬涓庝竴娆＄‘瀹氭€цВ鏋愶紱涓嶅畬鏁撮噸杞借鍒犻櫎锛岀敓浜ц皟鐢ㄦ棤娉曞啀鐪佺暐 item 灞傘€?
### 6. Config SSOT

娌℃湁鏂板閰嶇疆 schema銆備紭鍏堢骇 SSOT 鍥哄畾涓?`granted > item > form > base`锛岃緭鍏ヨ鐩栫户缁潵鑷湁鏁?`AbilityDefinition.InputBindingOverride`銆?
### 7. Red flag scan

- [x] 鏈柊澧?profile enum 鎴栬緭鍏?fallback
- [x] 鏈湪娑堣垂鑰呭鍒剁浜屽浼樺厛绾ф瀛?- [x] 涓嶅畬鏁?resolver 閲嶈浇宸插垹闄?- [x] 棰勭儹鍚庣殑杈撳叆瑕嗙洊瑙ｆ瀽 0 鍒嗛厤

### 8. Next variant test

鏂板鎶€鑳芥潵婧愬繀椤绘墿灞曞敮涓€ resolver 鍚堝悓鍜屽叏閾捐矾涓€鑷存€ф祴璇曪紝涓嶅緱鍙敼鍗曚釜娑堣垂鑰呫€?
---

## GAS Composition Gate 鈥?#651 Production Closeout

- **Task / Issue**: #651
- **Date**: 2026-07-12
- **Agent / Author**: Codex

### 1. Core judgment

涓昏浜や粯鐗╀负 A锛氭部鐜版湁 InputOrderMapping / OrderQueue 绠＄嚎璐┛ actor context 涓庣被鍨嬪寲鎺ュ崟缁撴灉锛涙病鏈夋柊澧為潰鏉夸笓鐢?order 绠＄嚎銆?
缁撹锛歅ASS銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer | 瀹炵幇杞戒綋 |
|---|---:|---|
| 绋嬪簭鍖栨縺娲?| N/A | `InputOrderMappingSystem.ActivateMappedAction` |
| 鎺ュ崟缁撴灉 | N/A | `OrderSubmitResult` + `OrderQueue.Submit` |
| 鐬勫噯涓讳綋鍥哄畾 | N/A | 鐜版湁 aiming state + `InputOrderActivationContext` |

### 3. Reuse list

- Handlers: typed `OrderSubmitHandler`
- Queues / Systems: `OrderQueue`, `CompositeOrderPlanner`, `InputOrderMappingSystem`
- Resolvers / Registries: existing mapping and command-intent routing
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A銆?
### 5. Transaction boundary

涓€娆℃縺娲诲浐瀹?actor/player锛涜繘鍏ョ瀯鍑嗗悗纭銆佸彇娑堝拰鎷掔粷缁х画浣跨敤璇ヤ笂涓嬫枃銆傛彁浜ょ粨鏋滄惡甯?actor銆乷rderId 鍜屽叡浜?`OrderSubmitResult`銆?
### 6. Config SSOT

娌℃湁鏂板閰嶇疆 schema锛沵apping銆乷rder type 鍜?actor source 缁х画鏉ヨ嚜鐜版湁姝ｅ紡閰嶇疆涓庢湇鍔°€?
### 7. Red flag scan

- [x] 鏃?actor 鐨勭▼搴忓寲鍏ュ彛宸插垹闄?- [x] `void OrderSubmitHandler` 宸插垹闄?- [x] 鏄惧紡 actor 涓嶅洖閫€ provider 鎴?collection fan-out
- [x] aiming actor 澶辨晥杩斿洖 `RejectedInvalidActor`

### 8. Next variant test

鏂板叆鍙ｅ繀椤昏繑鍥炲叡浜帴鍗曠粨鏋滐紝涓嶅緱鎭㈠ bool/void 鎴栦复鏃舵浛鎹?actor provider銆?
---

## GAS Composition Gate - #667 Tag Transaction Closeout

- **Task / Issue**: #667
- **Date**: 2026-07-12
- **Result**: PASS

### 1. Core judgment

The change extends the existing `TagOps`, `GameplayTagContainer`, `TagCountContainer`, and `DirtyFlags` contract with fixed-size value snapshots. It adds no tag DSL, effect preset, graph op, loader, or parallel tag runtime.

### 2. Reuse list

- Existing rule executor: `TagOps` and `TagRuleTransaction`
- Existing effect path: `EffectTagContributionHelper`
- Existing state: `GameplayTagContainer`, `TagCountContainer`, and `DirtyFlags`
- Existing diagnostics: `GasBudget.TagCountOverflowDropped`

### 3. Transaction boundary

One effect grant, revoke, or stack update snapshots all three fixed-size components before the first write. Any capacity or rule failure restores all snapshots and publishes one stable error. Rule-driven attach/remove work runs inside the same boundary.

### 4. Red flag scan

- [x] No dynamic collection or per-operation allocation added
- [x] No effect hot-path component add/remove remains in the contribution helper
- [x] No empty `TagOps` fallback remains in the three effect systems
- [x] Capacity failure is explicit and counted once per failed transaction

---

## GAS Composition Gate - #669 Audit Closeout

- **Task / Issue**: #669
- **Date**: 2026-07-13
- **Result**: PASS

### 1. Core judgment

The change closes six production-contract gaps inside the existing GAS, Order, template spawning, graph-output lifecycle, and Arch World destruction paths. It adds no gameplay DSL, effect preset, graph operation, loader, or parallel runtime pipeline.

### 2. Layer assignment

| Capability | Layer | Existing carrier |
|---|---:|---|
| Complete tag state before gameplay | N/A | `TagStateInstaller`, `EntityBuilder`, batch/runtime spawning |
| Sparse deferred-trigger collection | N/A | `DirtyFlags`, `TagOps`, `DeferredTriggerCollectionSystem` |
| Shared effect work budget | N/A | `EffectProcessingLoopSystem` and existing stage cursors |
| Cross-presentation admission generation | N/A | `OrderAdmissionResultBuffer` and SchemaUpdate owner |
| Owner-scoped graph output cleanup | N/A | Existing owner retirement notifications and output store |
| Unobserved destruction fast path | N/A | Existing Arch World entity-destroyed notification |

### 3. Reuse list

- Handlers: existing builtin effect handlers and lifecycle atomic handlers
- Queues / systems: existing effect proposal/application/lifetime loop, order intake, deferred triggers, entity spawning, and World destruction
- Resolvers / registries: existing `TagOps`, `TagRuleRegistry`, `EntityBuilder`, graph runtime API, and order registries
- Existing presets / graphs: unchanged; `WriteSelfAttribute` uses the existing graph instruction through the production graph API

### 4. New Layer 0 ops

N/A. No graph op, preset enum, gameplay profile, or configuration DSL was added.

### 5. Transaction boundary

Tag and attribute writes validate complete state before mutation. The fixed-capacity dirty-entity queue accepts each entity once per logic step. Capacity failure is explicit and restores tag, count, attribute, dirty, and presentation-visible state before returning control. Effect stages debit one shared remaining-work counter and retain their existing stage/pass cursors when exhausted. Admission generations have one SchemaUpdate owner.

### 6. Config SSOT

`game.json -> gasRuntimeCapacity.deferredTriggerActiveEntityCapacity` is the single production capacity source and is validated as positive during engine composition. This is a runtime budget, not gameplay behavior. The queue is registered once as `CoreServiceKeys.DirtyEntityQueue` and shared by the production `TagOps` and deferred-trigger collector.

### 7. Red flag scan

- [x] No gameplay DSL, preset, graph op, or parallel GAS/Order pipeline added
- [x] No effect hot-path component Add/Remove fallback remains
- [x] No permanent `DirtyFlags` full-world scan remains after one-time migration bootstrap
- [x] No validated batch component is silently omitted
- [x] No stage can independently consume the full outer effect budget
- [x] No admission producer or consumer owns an independent Clear boundary
- [x] No unobserved World destruction enters the subscriber lock
- [x] Fixed-capacity overflow is explicit and mutation paths roll back
- [x] Sparse dirty processing is zero-allocation after the complete active-path warmup

### 8. Next variant test

A new Mod or gameplay variant must install tag state through `TagStateInstaller`, mutate attributes through the shared production `TagOps` path, and rely on the same effect budget and order admission generation. It must not add a Mod-local dirty queue, fallback `TagOps`, alternate effect loop, or consumer-owned admission lifetime.

---

## GAS Composition Gate - #672-#680 Audit Closeout

- **Task / Issues**: #672, #673, #674, #675, #676, #677, #678, #679, #680
- **Date**: 2026-07-14
- **Agent / Author**: Codex
- **Result**: PASS

### 1. Core judgment

Primary delivery: A. Tighten existing Order, GAS, Graph, and Navigation contracts without adding a gameplay profile, preset switch, graph operation, loader, or parallel runtime.

The audit removes presentation-state fallback, repairs listener cache invalidation, makes graph paths blittable and deterministic, validates sink channels and GAS config at load time, removes legacy projectile and duplicate movement paths, deletes retired runtime types, freezes static registries, and moves long order paths into an explicit fixed-capacity component.

### 2. Issue and commit map

| Issue | Result | Commit |
|---|---|---|
| #672 | Order planning consumes only authoritative gameplay positions | `0e19772ed` |
| #673 | Response listener registration and removal invalidate the cache | `31b92da18` |
| #674 | Graph path ECS state is fixed-capacity and blittable | `ccbed8f4f` |
| #675 | Loaded graph construction and chunk events are deterministic | `fef4f394b` |
| #676 | Attribute sink channels fail during config load; hot paths do not validate strings | `5fef49d51` |
| #677 | Legacy projectile modes and implicit direction fallback are removed | `9a1b8ac62` |
| #678 | GAS config uses canonical required fields and load-time order ids | `4b2628769` |
| #679 | Duplicate ability movement runtime and Core business tags are removed | `9d3ed6ead` |
| #680 | Retired state is deleted; registries freeze; order path layout is compact | current issue commit |

### 3. Layer assignment

| Capability | Layer | Existing carrier |
|---|---:|---|
| Order position and path resolution | N/A | `OrderWorldSpatialResolver`, `OrderSubmitter`, `OrderBufferSystem` |
| Long-path storage and ownership | N/A | `OrderSpatialPayloadBuffer` authored on capable actors |
| Response listener lifecycle | N/A | `ResponseChainListenerOps` and existing effect request queue revision |
| Graph path execution | N/A | Existing `GraphPathfindingSystem` and graph stores |
| GAS config validation | N/A | Existing ability, effect, context, and attribute binding loaders |
| Ability and context id lifecycle | N/A | Existing static id registries with `Clear` / `Freeze` |

### 4. Reuse and new operations

- Handlers: existing projectile, order, effect, and ability handlers
- Queues / systems: existing Order queue/buffer/finalizer, effect request queue, response processing, and graph pathfinding system
- Resolvers / registries: existing order spatial resolver, order type registry, ability form set registry, and context group registry
- Existing presets / graphs: unchanged
- New Layer 0 graph/effect operations: N/A
- New gameplay schema: NO

`OrderSpatialPayloadOps` is an ownership helper for an existing Order value. It does not interpret gameplay or create a second order pipeline.

### 5. Transaction and ownership boundary

Long order paths use one fixed-capacity `OrderSpatialPayloadBuffer` per capable actor. An order owns exactly one generation-checked slot while it is in global intake, active, queued, pending, or being rebuilt.

The owner releases the slot on intake rejection, queue expiration/removal, pending replacement/expiration, active finalization, cancel-all, road replan replacement, or failed road enqueue. Public buffer and queue clearing APIs reject payload-bearing orders when no `World` owner is available. Missing buffers, exhausted slots, stale handles, point-count mismatches, and blackboard capacity failures are explicit errors; none silently truncate or leak.

### 6. Config SSOT

- Business blackboard keys are declared by `GAS/order_types.json`, not hard-coded in Core.
- Ability form set and context group ids are declared during config load, then frozen.
- Projectile modes, ability execution fields, order references, and attribute sink channels have one canonical name and load-time validation.
- Runtime capacity is defined by fixed component constants derived from the formal Order slots; no dynamic allocation or hidden global pool is used.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, order, graph, or movement runtime added
- [x] No presentation component is used as gameplay position truth
- [x] No legacy projectile compatibility mapping remains
- [x] No silent config default, unknown order key, path truncation, or capacity drop remains
- [x] No managed ECS path component or hot-path growth remains
- [x] No long-path ECS component is added or removed in the hot path
- [x] Every path ownership exit has an explicit release or hard failure

### 8. Historical verification snapshot

The figures below belong to the earlier #644-#680 delivery snapshot. They are retained as historical evidence and must not be used as the acceptance result for the current PR HEAD.

- #680 focused acceptance, including direct payload lifecycle tests: 41 / 41
- GAS architecture workflow guard: 45 / 45
- Association workflow slice: 127 / 127
- Raylib field workflow slice: 4 / 4
- Web UI Panel Kit tests: PASS
- Maintained `src/Tests` project graphs: all build successfully
- Production Mod smoke: 34 / 35; the only failure is the existing CameraAcceptance unbound local-player fixture
- Full GasTests at that snapshot: 1729 passed, 82 failures, 1 skipped
- Full ArchitectureTests after the two Order fixture corrections: 136 passed, 2 unrelated repository-wide legacy-token scan failures
- `git diff --check`: PASS

### 9. Next variant test

A new Order variant must reuse `OrderSpatialPayloadOps` and the existing Order finalizer. A new GAS variant must change graph wiring or effect steps. Neither may add a Core gameplay enum, fallback field name, Mod-local path pool, or alternate movement/order runtime.

---

## GAS Composition Gate - PR #660 Fan-Out Budget Diagnostics Repair

- **Task / Issue**: PR #660 audit repair for persistent fan-out budget diagnostics and retired callback state
- **Date**: 2026-07-18
- **Agent / Author**: Codex gas-diagnostics agent

### 1. Core judgment

Primary delivery: A. Repair the existing builtin fan-out accounting path and make one root budget span the complete effect-processing transaction.

Result: PASS

Reason: The change reuses `BuiltinHandlerExecutionContext.DroppedCount`, `RootBudgetTable`, `GasBudget`, `GasBudgetReportSystem`, and `EffectProcessingLoopSystem`. It adds no gameplay variant, graph op, preset switch, profile field, registry, loader, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Root fan-out admission | N/A | Existing `RootBudgetTable` shared by the effect loop |
| Per-phase dropped accounting | N/A | Existing `BuiltinHandlerExecutionContext.DroppedCount` and `GasBudget` |
| Structured reporting | N/A | Existing `GasBudgetReportSystem` and `GasDiagnosticEventBuffer` |
| Retired callback cleanup | N/A | Existing application/lifetime systems |

### 3. Reuse list

- Handlers: existing `SpatialQuery`, `DispatchPayload`, and `ReResolveAndDispatch` builtin handlers
- Queues / Systems: `EffectProcessingLoopSystem`, proposal/application/lifetime systems, `EffectRequestQueue`, `GasBudgetReportSystem`
- Resolvers / Registries: `TargetResolverFanOutHelper`, existing `RootBudgetTable`, existing handler/template registries
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No atomic operation, graph operation, preset, registry, or schema was added.

### 5. Transaction boundary

One `EffectProcessingLoopSystem` transaction advances one shared `RootBudgetTable` generation before its first proposal pass. Proposal, application, lifetime, and all follow-up passes consume that same generation. Every proposal-phase builtin flush, including the instant phase chain and `OnCalculate`, drains `BuiltinHandlerExecutionContext.DroppedCount` into `GasBudget` before the runtime is reset. Independently constructed systems retain a private table and advance it only when beginning their own slice.

### 6. Config SSOT

Behavior remains in the existing effect template and graph configuration. No JSON schema or config field was added.

### 7. Red flag scan

- [x] No profile inherit/placement enum added
- [x] No parallel spawn, effect, or diagnostics pipeline added
- [x] No placement validation moved into lifecycle operations
- [x] No fallback or compatibility alias added
- [x] No new handler, graph op, preset, registry, or schema added
- [x] Retired callback command/list/stage/budget state was deleted rather than preserved

### 8. Next variant test

A new Mod fan-out variant changes an effect step or graph connection and continues through the same builtin handler, root budget, and structured diagnostics path. It does not add a Core enum or alternate budget pipeline.

---

## GAS Composition Gate - Issues #686, #687, #688

- **Task / Issues**: #686 invalid transactional attribute target, #687 continuation-state preinstallation, #688 continuation payload ownership
- **Date**: 2026-07-18
- **Agent / Author**: Codex

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A锛堜慨澶嶇幇鏈?effect transaction 涓?Order continuation 绠＄嚎鐨勬牎楠屻€侀瑁呭拰 rollback/ownership 鍚堝悓锛涗笉鏂板鐜╂硶鍙樹綋锛?
缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 淇敼浠呮敹绱х幇鏈?`EffectPhaseSideEffectTransaction`銆乣EntityRuntimeStatePlan`銆乣CompositeOrderPlanner`銆乣OrderSubmitter` 鍜?`OrderContinuationSystem`锛屼笉鏂板 graph op銆乸reset銆乸rofile銆乺egistry銆乴oader 鎴栧钩琛岃繍琛屾椂銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| persistent effect side-effect rollback | 1 | 鐜版湁 `EffectPhaseSideEffectTransaction` + `EffectApplicationSystem` |
| order runtime-state assembly | N/A | 鐜版湁 `EntityRuntimeStatePlan`銆乻calar/batch/runtime spawn |
| continuation payload ownership | N/A | 鐜版湁 `OrderContinuationBuffer`銆乣OrderSubmitter`銆乣OrderContinuationSystem`銆乣CompositeOrderPlanner` |

### 3. Reuse list

- Handlers: 鐜版湁 Graph `ModifyAttributeAdd` / `ModifyAttributeSet` 鍏ュ彛
- Queues / Systems: `EffectApplicationSystem`銆乣OrderQueue`銆乣OrderContinuationSystem`
- Resolvers / Registries: `EntityRuntimeStatePlan`銆乣OrderTypeRegistry`
- Existing presets / graphs: 涓嶅彉

### 4. New Layer 0 ops (if any)

N/A銆傛病鏈夋柊澧?handler銆乬raph op 鎴栫敓鍛藉懆鏈熷師瀛愭搷浣溿€?
### 5. Transaction boundary

蹇呴』鍘熷瓙 rollback 鐨勬楠? persistent effect 鐨勫叏閮?staged 灞炴€с€佹爣绛俱€乫an-out銆乴istener銆乸resentation/event side effects锛涗换涓€鏃犳晥灞炴€х洰鏍囧繀椤诲湪 commit 鍓嶅け璐ュ苟鐢辩幇鏈?application rollback 鎾ら攢銆侽rder continuation 鍦ㄦ敞鍐屽悗鎸佹湁 follow-up payload锛汧ailed/Cancelled銆乺egistration failure 鎴?primary admission failure蹇呴』閲婃斁涓€娆★紝Completed 浠呮妸鎵€鏈夋潈杞Щ鍒版寮?Order submission 閾俱€?
### 6. Config SSOT

琛屼负閰嶇疆浠嶄綅浜庣幇鏈?effect template/graph 涓?order type catalog銆?
鏄惁鏂板 JSON schema: NO

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn/effect/order 骞宠鐨勮繍琛屾椂绠＄嚎
- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犻粯璁?fallback 鎴栧吋瀹规梺璺?- [x] 鏈柊澧?registry銆乸reset 鎴?schema

### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: graph 杩炵嚎 / effect 姝ラ锛汷rder 鍙樹綋缁х画浣跨敤鍚屼竴 runtime-state installer銆乧ontinuation buffer 鍜?terminal-result 绠＄嚎锛屼笉淇敼 Core gameplay enum銆?
---

## GAS Composition Gate - PR #660 Lifetime Phase Atomicity

- **Task / Issue**: PR #660 audit repair for `OnPeriod`, `OnExpire`, and `OnRemove` transaction coverage and pacemaker reset safety
- **Date**: 2026-07-19
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Extend the existing effect side-effect transaction to the existing lifetime phase execution path.

Result: PASS

Reason: The repair reuses `EffectPhaseSideEffectTransaction`, `GasGraphRuntimeApi`, `EffectLifetimeSystem`, and the existing effect/event/spawn/presentation queues. It adds no gameplay variant, graph op, preset switch, profile field, registry, loader, fallback, or parallel runtime.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Lifetime graph side-effect staging | 1 | Existing `EffectPhaseSideEffectTransaction` bound by `EffectLifetimeSystem` |
| Lifetime scan rollback | 1 | Existing effect, target, presentation, and dirty-queue checkpoints |
| Listener owner removal | 1 | Fixed-capacity listener staging in `EffectPhaseSideEffectTransaction` |
| Pacemaker reset after commit | 1 | Existing `EffectProcessingLoopSystem.ResetSlice` and lifetime committed-cleanup tail |
| Mod behavior | 2 | Existing effect templates and phase graph bindings |

### 3. Reuse list

- Handlers: existing GAS graph and builtin handlers, including attribute, event, effect-request, and fan-out operations
- Queues / Systems: existing effect request, spawn, gameplay event, presentation, dirty-entity, lifetime, effect-loop, and pacemaker paths
- Resolvers / Registries: existing template, graph, preset, builtin-handler, condition, tag-rule, and root-budget registries
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No graph operation, effect operation, handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

One lifetime slice opens one existing `EffectPhaseSideEffectTransaction` after the bounded ECS snapshot scan. `OnPeriod`, `OnExpire`, and `OnRemove` graph/builtin side effects, fan-out effect requests, and listener owner removals remain staged until all lifetime phase entries succeed. Any failure rolls back staged attributes, gameplay events, effect requests, listener changes, presentation writes, dirty-queue writes, effect timers, granted tags, and active-effect container mutations.

Reset before commit rolls back the slice. Aggregate-dirty changes commit inside the transaction; reset after commit completes only the bounded effect-destruction tail, so the pacemaker budget fuse cannot convert a diagnostic halt into an exception or abandon partial cleanup.

All staging is fixed-capacity. Listener removals are deduplicated by `(entity, ownerEffectId)` so `OnExpire` plus `OnRemove` does not consume the same ownership slot twice. Capacity exhaustion remains an explicit error.

### 6. Config SSOT

Behavior remains in the existing effect template and phase graph assets. No JSON schema or configuration field was added.

### 7. Red flag scan

- [x] No profile inherit, placement, or preset enum added
- [x] No parallel effect, lifecycle, transaction, or reset pipeline added
- [x] No lifecycle behavior was moved into a new graph op
- [x] No fallback, compatibility bypass, silent drop, or dynamic hot-path growth added
- [x] No new registry, loader, or configuration SSOT added
- [x] Failure coverage distinguishes the pre-fix direct writes from transactional rollback for all three lifetime phases
- [x] Production pacemaker reset coverage exercises the real effect-processing loop

### 8. Next variant test

A new Mod lifetime variant changes an effect template or phase graph connection and automatically uses the same lifetime transaction. It does not add a Core enum, preset switch, alternate transaction, or compatibility path.

---

## GAS Composition Gate - PR #660 Collection Performer Lifecycle

- **Task / Issue**: PR #660 main merge repair for collection-member highlight lifetime and performer implicit parent resolution
- **Date**: 2026-07-24
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Repair existing performer rule command composition and runtime lifecycle semantics for entity collection presentation events.

Result: PASS

Reason: The change reuses `EntityCollectionPresentationEventSystem`, `PerformerRuleSystem`, `PerformerRuntimeSystem`, `PerformerEntityRuntime`, and `PresentationOwnerHasPerformerPayload`. It adds no graph op, effect preset, profile field, loader, registry, fallback path, or parallel presentation pipeline.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Collection member event command emission | N/A | Existing `PerformerRuleSystem` |
| Persistent scoped performer idempotent update | N/A | Existing `PerformerRuntimeSystem` duplicate scoped create path |
| Owner root payload parent lookup | N/A | Existing `PresentationOwnerHasPerformerPayload` marker and `PerformerState` identity |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing collection event, performer rule, performer runtime, and performer behavior systems
- Resolvers / Registries: existing performer definition registry, entity collection key registry, and owner payload marker
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No lifecycle atomic op, graph operation, effect handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

N/A. This repair does not introduce a mutation transaction. The lifecycle boundary remains the existing performer command tick: collection row removal emits a scoped destroy for the removed row, while a metadata re-add for a surviving row must resolve to the same persistent scoped performer and update in place.

### 6. Config SSOT

Behavior remains in existing performer definition rules and collection key registration.

New JSON schema: NO.

### 7. Red flag scan

- [x] No profile inherit, placement, or preset enum added
- [x] No parallel collection, performer, or lifecycle pipeline added
- [x] No owner/parent fallback or silent create bypass added
- [x] No hard-coded definition id or implicit numeric registry slot added
- [x] No hot-path structural mutation pattern added

### 8. Next variant test

A new Mod collection highlight variant changes performer definition rules or collection event bindings. It continues through the same collection event stream and performer command runtime; it does not add a Core enum, alternate parent resolver, or compatibility path.

---

## GAS Composition Gate - PR #660 Self Regression Test Isolation

- **Task / Issue**: PR #660 self-regression cleanup after full GasTests / merge-base TRX comparison
- **Date**: 2026-07-23
- **Agent / Author**: Codex supervising Cursor

### 1. Core judgment

鏂板彉浣撲富瑕佷氦浠樼墿鏄紙A/B/C/D锛? A锛堟敹绱х幇鏈?GAS 娴嬭瘯鐨?registry / attribute SSOT 浣跨敤锛涗笉鏂板 gameplay 鍙樹綋锛?
缁撹: PASS

涓€鍙ヨ瘽鐞嗙敱: 淇敼鍙 PR 鏂板/鏀硅繃鐨?GAS 娴嬭瘯鏄惧紡闅旂鍏ㄥ眬 id registry锛屽苟閫氳繃 `AttributeRegistry` 浣跨敤娴嬭瘯涓撳睘灞炴€?id锛涙病鏈夋柊澧?graph op銆乪ffect preset銆乸rofile 瀛楁銆乴oader銆乺egistry 鎴栧钩琛岃繍琛屾椂銆?
### 2. Layer assignment

| 姝ラ/鑳藉姏 | Layer (0/1/2/3) | 瀹炵幇杞戒綋 |
|-----------|-----------------|----------|
| Ability form set 娴嬭瘯闅旂 | N/A | `TagStateInstallationContractTests` fixture setup/teardown |
| Mud demo 娴嬭瘯灞炴€?SSOT | N/A | `AttributeRegistry` + `AttributeBuffer.SetBase` |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing Ability / Effect phase test systems unchanged
- Resolvers / Registries: existing `AbilityFormSetIdRegistry`, `AttributeRegistry`
- Existing presets / graphs: existing test graph instructions unchanged except attribute id source

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

蹇呴』鍘熷瓙 rollback 鐨勬楠? N/A锛涙湰娆′笉鏀圭敓浜?mutation transaction銆傛祴璇曞彧娑堥櫎璺ㄧ敤渚嬪叏灞€鐘舵€佹薄鏌擄紝閬垮厤 full suite 涓?registry freeze 鍜?attribute constraints 褰卞搷鍚庣画鐢ㄤ緥銆?
### 6. Config SSOT

琛屼负閰嶇疆钀藉湪: existing test graph instructions and registry ids.

鏄惁鏂板 JSON schema: NO

### 7. Red flag scan

- [x] 鏈柊澧?profile inherit/placement enum
- [x] 鏈柊寤轰笌 spawn/effect/order 骞宠鐨勮繍琛屾椂绠＄嚎
- [x] 鏈妸 placement 鏍￠獙濉炶繘 lifecycle op
- [x] 鏈坊鍔犻粯璁?fallback 鎴栧吋瀹规梺璺?- [x] 鏈柊澧?registry銆乸reset 鎴?schema

### 8. Next variant test

銆屼笅涓€涓?Mod 鍙樹綋銆嶅皢淇敼: graph 杩炵嚎 / effect 姝ラ銆傛祴璇曡嫢闇€瑕佸睘鎬ф垨 form set id锛岀户缁粠姝ｅ紡 registry 鍙?id锛屼笉纭紪鐮佸叡浜叏灞€妲戒綅銆?

---

## GAS Composition Gate - PR #660 MassNavigation Spawn Membership Follow-up

- **Task / Issue**: PR #660 / #689 follow-up after GitHub `solution-verify` MassNavigation PR acceptance failure at `49a81360`
- **Date**: 2026-07-25
- **Agent / Author**: Codex

### 1. Core judgment

Primary delivery: A. Repair existing runtime entity spawn relationship preflight semantics.

Result: PASS

Reason: Explicit `MembershipTarget` spawn requests now remain part of the preflight relationship plan even when the template/request does not author `Team`. This reuses the existing `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`, `RelationshipRuntime`, and `MemberOf` relationship type. No fallback path, graph op, effect preset, schema, registry, or parallel spawn pipeline was added.

### 2. Layer assignment

| Step / capability | Layer | Implementation carrier |
|---|---:|---|
| Runtime spawn relationship plan | N/A | Existing `RuntimeEntitySpawnSystem.PreflightSpawnRelationships` |
| Explicit member-of commit | N/A | Existing `RelationshipRuntime.EnsureLink` via `ApplyRelationshipPlan` |
| Regression coverage | N/A | Existing `TagEffectArchitectureTests` runtime spawn harness |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: existing `RuntimeEntitySpawnQueue`, `RuntimeEntitySpawnSystem`
- Resolvers / Registries: existing `RelationshipRuntime`, `RelationshipTypeRegistry`, `TeamEntityLookup`
- Existing presets / graphs: unchanged

### 4. New Layer 0 ops

N/A. No graph operation, effect operation, handler, preset, registry, schema, or materialization path was added.

### 5. Transaction boundary

Spawn preflight still validates relationship service availability and target liveness before dequeue. The follow-up only restores explicit relationship intent into the post-create relationship plan. If capacity/service validation fails, the request remains queued; if creation succeeds, each spawned entity receives the explicit `MemberOf` edge through the existing relationship runtime.

### 6. Config SSOT

Behavior remains in the existing runtime spawn request contract. No JSON schema or configuration field was added.

### 7. Red flag scan

- [x] No profile inherit, placement, or preset enum added
- [x] No parallel spawn, relationship, or MassNavigation pipeline added
- [x] No fallback, compatibility bypass, silent drop, or dynamic hot-path growth added
- [x] No new registry, loader, or configuration SSOT added
- [x] Regression covers the pre-fix explicit `MembershipTarget` without authored `Team` case

### 8. Next variant test

A new runtime spawn variant can author `Team`, pass explicit `MembershipTarget`, or both. Explicit membership remains authoritative, and `Team` is only used for consistency validation when present.
