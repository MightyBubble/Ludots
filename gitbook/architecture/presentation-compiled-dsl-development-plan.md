# 表现层编译式 DSL 开发计划

本文定义 [表现层编译式 DSL 架构](presentation-compiled-dsl-architecture.md) 的正式实施计划。目标是把当前 performer runtime 路线收束为 owner-level backend runtime，并在 30K owner / 200K recipe 目标下建立可持续的 TDD 与性能守卫。

## 1 交付目标

- 正式移除 `1 performer = 1 runtime entity` 的主线实现方向
- 建立 authoring parser、semantic validator、compiler、artifact registry、owner runtime store
- 用 typed projection backend 替代 generic performer behavior/emit interpreter
- 保留现有 `performers.json` authoring 面，但把其运行时含义完全改写为 compiled DSL
- 在 steady-state static 场景做到无行为求值、无分配、无 definition scan

## 2 开发原则

- 先建 compiler 和测试守卫，再替换 runtime
- 不允许引入兼容性 fallback，同时保留两套正式 runtime 真相
- 每个阶段都必须有红灯测试和性能门禁
- 新旧实现共存只能作为短期迁移桥，最终产物只能保留 compiled DSL runtime

## 3 依赖图

```text
P0 文档冻结与守卫
  ↓
P1 Authoring AST + Normalize + Validate
  ↓
P2 Typed IR + Artifact Registry
  ↓
P3 Owner Runtime Store + Param/Anchor Pages
  ↓
P4 Dirty Route Engine
  ↓
P5 Projection Backend (Mesh/Hud/Text)
  ↓
P6 Tick Backend (Animator/Spline/Sound)
  ↓
P7 Showcase/UAT/Benchmark 迁移
  ↓
P8 删除 legacy performer runtime
```

## 4 分阶段计划

### P0 文档冻结与测试护栏

目标：

- 冻结旧 performer runtime 口径
- 建立新架构文档、开发计划、迁移计划
- 新增“不允许 runtime performer entity”守卫测试骨架

产出文件：

- `gitbook/architecture/presentation-compiled-dsl-architecture.md`
- `gitbook/architecture/presentation-compiled-dsl-development-plan.md`
- `gitbook/architecture/presentation-compiled-dsl-migration-plan.md`
- `src/Tests/PresentationTests/PresentationCompiledDslContractTests.cs`

必须通过：

- `PresentationCompiledDslContractTests.LegacyRuntimePerformerModel_IsNotFormalMainline`
- `PresentationCompiledDslContractTests.GitBookPointsToCompiledDslPages`

### P1 Authoring Front-End

目标：

- 将 `performers.json` 解析收束为纯 authoring AST
- 把 `extends`、children、默认值、alias 规范化进 front-end
- 建立 scope/export/read 合同校验

建议文件：

- `src/Core/Presentation/Compiler/PresentationDslAst.cs`
- `src/Core/Presentation/Compiler/PresentationDslParser.cs`
- `src/Core/Presentation/Compiler/PresentationDslNormalizer.cs`
- `src/Core/Presentation/Compiler/PresentationDslSemanticValidator.cs`

必须通过：

- `PresentationDslParserTests.ParsesBlacksmithAuthoringIntoAst`
- `PresentationDslSemanticTests.ScopeReadWithoutExport_Fails`
- `PresentationDslSemanticTests.AnchorCycle_Fails`
- `PresentationDslSemanticTests.UnlowerableRecipe_Fails`

退出条件：

- front-end 不再生成任何运行时 `PerformerCommand`
- children 和 scope 仍可表达 authoring 语义，但无 runtime node 含义

### P2 Typed IR 与 Artifact Registry

目标：

- 把 authoring lower 为 typed IR 与 packed artifact
- 建立 `PresentationArchetypeArtifact`
- 建立 attr/tag/event route table

建议文件：

- `src/Core/Presentation/Compiler/PresentationTypedIr.cs`
- `src/Core/Presentation/Compiler/PresentationArchetypeCompiler.cs`
- `src/Core/Presentation/Compiler/PresentationArtifactRegistry.cs`
- `src/Core/Presentation/Compiler/PresentationRoutePacker.cs`

必须通过：

- `PresentationArchetypeCompilerTests.Blacksmith_LowersToTypedRecipes`
- `PresentationArchetypeCompilerTests.Children_CompileToAnchorGraph`
- `PresentationArchetypeCompilerTests.AttributeDirty_RouteSpanIsOwnerLocal`
- `PresentationArchetypeCompilerTests.ScopeDeactivate_CompilesToLocalMaskAndSlices`

退出条件：

- 编译产物不再包含 `PerformerInstance`、`ParentHandle`、`BehaviorActiveMask`
- route table 不再引用 definition scan 或 scope query

### P3 Owner Runtime Store

目标：

- 建立单 owner 句柄的 runtime store
- 建立 dense param pages、anchor pages、projection cache pages
- 用 materialize/destroy owner 语义替代 create/destroy performer

建议文件：

- `src/Core/Presentation/Runtime/OwnerPresentationRuntime.cs`
- `src/Core/Presentation/Runtime/OwnerPresentationRuntimeStore.cs`
- `src/Core/Presentation/Runtime/PresentationParamPages.cs`
- `src/Core/Presentation/Runtime/PresentationAnchorPages.cs`
- `src/Core/Presentation/Systems/PresentationOwnerMaterializeSystem.cs`

必须通过：

- `OwnerPresentationRuntimeStoreTests.SpawnOwner_AllocatesSingleHandle`
- `OwnerPresentationRuntimeStoreTests.DestroyOwner_ReleasesPagesWithoutTreeWalk`
- `OwnerPresentationRuntimeStoreTests.ScopeMask_TogglesLocalSlicesOnly`

退出条件：

- 单个 owner 没有附带 runtime performer entity
- owner destroy 不需要递归销毁 runtime subtree

### P4 Dirty Route Engine

目标：

- 将 `PresentationBridgeSystem` 的 owner dirty feed接到 route engine
- 只按 owner 和 route span 执行属性/标签/事件变化
- 建立 projection dirty bit 和 tick owner set

建议文件：

- `src/Core/Presentation/Runtime/PresentationDirtyRouteExecutor.cs`
- `src/Core/Presentation/Systems/PresentationDirtyApplySystem.cs`
- `src/Core/Presentation/Runtime/PresentationDirtyBitset.cs`

必须通过：

- `PresentationDirtyRouteTests.DurabilityDirty_OnlyTouchesMeshHudAndTextRoutes`
- `PresentationDirtyRouteTests.WorkingTagToggle_OnlyTouchesWorkingScope`
- `PresentationDirtyRouteTests.NoDefinitionScan_NoGlobalScopeQuery`

退出条件：

- 删除或旁路 `PerformerBehaviorSystem` 的 definition scan 主路
- owner dirty 处理复杂度与 affected route span 成正比

### P5 Projection Backend

目标：

- 建立 mesh/hud/text/spline/ground overlay 的 typed projection backend
- `WorldHudToScreenSystem` 继续作为 screen projection 阶段
- `ProjectionLane` 只读 artifact + owner runtime，不解释 behavior

建议文件：

- `src/Core/Presentation/Runtime/Projection/PresentationMeshProjectionRuntime.cs`
- `src/Core/Presentation/Runtime/Projection/PresentationHudProjectionRuntime.cs`
- `src/Core/Presentation/Runtime/Projection/PresentationTextProjectionRuntime.cs`
- `src/Core/Presentation/Systems/PresentationProjectionSystem.cs`

必须通过：

- `PresentationProjectionTests.MeshRecipe_ProjectsToVisualProxy`
- `PresentationProjectionTests.HudBarRecipe_ProjectsToWorldHudItem`
- `PresentationProjectionTests.TextRecipe_ProjectsToWorldTextItem`
- `PresentationProjectionTests.StableProjection_ReusesCacheWithoutReeval`

退出条件：

- `PerformerEmitSystem` 不再是正式主线
- HUD/text 与 mesh 走 typed backend，而不是 performer asset switch

### P6 Tick Backend

目标：

- 把 animator/spline/sound 从 generic behavior loop 拆到 tick backend
- 只对 active tick owners 执行 tick programs
- 保持 animator packed state、sound request 等 adapter-facing contract

建议文件：

- `src/Core/Presentation/Runtime/Tick/PresentationAnimatorRuntime.cs`
- `src/Core/Presentation/Runtime/Tick/PresentationSplineRuntime.cs`
- `src/Core/Presentation/Runtime/Tick/PresentationSoundRuntime.cs`
- `src/Core/Presentation/Systems/PresentationTickSystem.cs`

必须通过：

- `PresentationTickTests.AnimatorProgram_TicksOnlyActiveOwners`
- `PresentationTickTests.SplineProgram_UpdatesAnchorPage`
- `PresentationTickTests.SoundRecipe_EmitsEventDrivenRequests`

退出条件：

- `PerformerAnimatorStateBuffer` 若保留，必须降级为 typed backend buffer
- tick 路线不依赖 runtime performer entity

### P7 Showcase、UAT 与 Benchmark 迁移

目标：

- 用 owner-centric fixture 替换 current performer fixture
- 保留黑铁匠铺语义与 30K/200K 压测
- 新增 compiled DSL 规模守卫

建议测试：

- `BlacksmithCompiledPresentationUatTests`
- `PresentationCompiledDslBenchmarkTests`
- `PresentationScaleGuardTests`

必须通过：

- `BlacksmithCompiledPresentationUatTests.DurabilityHalf_UpdatesMeshHudAndText`
- `BlacksmithCompiledPresentationUatTests.WorkingTagToggle_OnlyChangesWorkingScope`
- `PresentationScaleGuardTests.ThirtyThousandOwners_TwoHundredThousandRecipes_NoRuntimePerformerEntities`
- `PresentationScaleGuardTests.SteadyStateStatic_NoBehaviorEvalNoAlloc`

退出条件：

- 黑铁匠铺 acceptance fixture 不再依赖 `PerformerEntityRuntime`
- benchmark 统计项包含 `RuntimePerformerEntityCount=0`

### P8 删除 Legacy Performer Runtime

目标：

- 删除旧 runtime performer 路线
- 清理 GitBook 历史页面中的迁移状态
- 完成 API 和测试收束

必须删除或降级：

- `PerformerEntityRuntime`
- `PerformerRuntimeSystem`
- `PerformerBehaviorSystem`
- `PerformerEmitSystem`
- `PerformerParamResolver`
- `PerformerState` 及其 runtime components

必须通过：

- 全量 `dotnet build`
- Presentation tests 全绿
- scale guard 全绿
- 无 `PerformerState` 查询留在正式主线

## 5 性能门禁

每个阶段都必须记录以下门禁：

| 门禁 | 目标 |
|------|------|
| `RuntimePerformerEntityCount` | 恒为 0 |
| `DefinitionScanCount` | 恒为 0 |
| `GlobalScopeQueryCount` | 恒为 0 |
| `GCAllocBytesPerFrame` | steady-state 为 0 |
| `DirtyRouteFallbackCount` | 恒为 0 |
| `ProjectionStableCacheHitRate` | 稳态场景高于 95% |

30K / 200K 目标下的最低验收：

- 30K owner 全部在场
- 200K compiled recipes
- 500 dirty owners / frame
- HUD/text/mesh 同时启用
- 主线程无 definition scan 和 global scope query

## 6 测试目录建议

- `src/Tests/PresentationTests/Compiler/`
- `src/Tests/PresentationTests/Runtime/`
- `src/Tests/PresentationTests/Projection/`
- `src/Tests/PresentationTests/Scale/`

建议测试类：

- `PresentationDslParserTests`
- `PresentationDslSemanticTests`
- `PresentationArchetypeCompilerTests`
- `OwnerPresentationRuntimeStoreTests`
- `PresentationDirtyRouteTests`
- `PresentationProjectionTests`
- `PresentationTickTests`
- `BlacksmithCompiledPresentationUatTests`
- `PresentationScaleGuardTests`

## 7 与旧看板关系

旧 `performer-development-kanban.md` 中的 T4-T25 路线不再是正式主线。后续工作必须以上述 P0-P8 为准；旧看板仅作为历史实现证据保留。
