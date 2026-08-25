# Presenter 业务 visibility 收敛到 Param/Behavior/Command（#1093）

## Scenario Card
- Player goal: presenter 的业务可见性只有一个真相——Param/Behavior/Command；EmitSystem 只做平台 culling。
- 场景: `src/Tests/PresentationTests/Presenter/PresenterVisibilityConvergenceTests.cs` + `PresenterDefinitionConfigLoaderTests`（真实 JSON 加载管线）。
- Build: local PresentationTests / GasTests, branch `codex/issue-1093-visibility-param-behavior`（基线 c497ea5e29）。

## Timeline
- [T+001] `definition_authoring` -> 顶层 `visibility` 字段在 `PresenterDefinitionConfigLoader.RejectRemovedFields` 硬拒，报错指路 Param/Behavior/Command 等价写法。
- [T+002] `definition_authoring` -> `AssetBinding.visibilityParamKey` 若无本定义内的 Int 生产者（Int paramDefault / binding / SetParam rule / TagBinding / attribute threshold），加载期硬拒。
- [T+003] `param_driven_visibility` -> `visconv.visible=1` 发射 1 个可见请求；`0` 隐藏；`1` 恢复。业务可见性完全由 Int param 回放。
- [T+004] `command_driven_visibility` -> `DeactivateBehavior`/`ActivateBehavior` 命令翻 active mask，槽位发射随 mask 停止/恢复。
- [T+005] `culling_independence` -> 平台裁剪（OwnerCullVisible=false + LOD=Culled）阻断发射，但不改 active mask、不改 visibility param；业务隐藏（param=0）在裁剪开/关两种状态下都保持隐藏。两栏状态见 trace.jsonl 的 `platform_cull` / `business_visible` 列。
- [T+006] `emit_system_branch_removal` -> `PresenterEmitSystem` 的 `EvaluateVisibility`/`IsSolePossessedRep`/`IsOwnerCullVisible`/`OwnerSatisfiesAttributeRequirements` 分支全部删除；`PresenterDefinition.VisibilityCondition`、`PresenterEmitCache.LastDefinitionVisible` 一并摘除；`InlineConditionKind` 保留给 Rule/bootstrap 条件消费面（无死代码）。
- [T+007] `existing_visibility_param_regression` -> 全仓 47 个 presenters.json 的 visibilityParamKey 均为 `none` 或有 Int 生产者；MassNavigation / LSW chill bar 等既有场景回归通过。
- [T+008] `authoring_migration` -> 全仓 8 个 mod 的 presenters.json 顶层 `visibility` 使用点逐个迁移（LudotsCoreMod/moba/rts/capability_standard×4/schema fixture），语义保真：`OwnerCullVisible` 由平台 culling 自动承担，`None` 直接删除。

## Outcome
- success: yes
- EmitSystem 不再解释任何业务 visibility condition；业务可见性可从 Param/Behavior/Command trace 完整回放。
- 旧顶层 condition 与无生产者的 visibilityParamKey 都在加载期明确失败，不静默迁移。
- culling 开关不改变业务可见性（active mask 与 param 不动）。
- 证据: `artifacts/evidence/presenter-1093/trace.jsonl`（平台/业务分栏）。

## Summary Stats
- targeted: PresenterVisibilityConvergenceTests + PresenterDefinitionConfigLoaderTests = 158 passed, 0 failed
- targeted: GasTests (Ludots.Tests.Presentation) = 80 passed, 0 failed
- full: PresentationTests = 785 passed / 11 failed；失败均为 main 既有：4 个 loader 临时目录用例在干净基线 c497ea5e29 复现，其余为 showcase 输入模拟 flakes（两轮 20/11 波动）
- benchmark: presenter-timer 30k×1 emit 均值 0.6746ms -> 0.3047ms（删除每帧业务求值后自然收益）
