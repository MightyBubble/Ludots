# Skia GPU UI Compositor — main 基线爆红记录

- 基线 commit：`23deddf07f`（origin/main，2026-09-21）
- 工作树：`C:\001_AI\LudotsProd-skia-gpu`，分支 `skia-gpu-ui-compositor` 开出前在干净 main 上跑出
- 命令：`dotnet test src/Tests/<Project>/<Project>.csproj -c Debug`（本地 Windows，真实 GPU）
- 用途：本 PR 合并时对照排除，避免把下列既有失败误报成本 PR 引入；新失败不在此列的才是本 PR 的责任

## 结果

| 套件 | 结果 |
|---|---|
| PresentationTests | 13 失败 / 1154 通过 |
| UiShowcaseTests | 全通过 |
| RaylibAdapterTests | 全通过 |
| ArchitectureTests | 全通过 |

## PresentationTests 既有失败清单（13）

| 用例 | 类别 | 失败摘要 |
|---|---|---|
| `BatchInterpolation_10kMovingAgents_SteadyStateMedianBudgetAndZeroAllocation` | 性能预算 | `median <= 0.8ms` 断言超预算 |
| `Benchmark_SkiaOverlay_10kHudAndText_Writes120HzReport` | 性能预算 | `valueChurnTextOnly` 未低于 `barsOnly * 1.5` |
| `Showcase_SteadyStateSimTickBudget_CadenceAgentSliceAmortization` | 性能预算 | `P95Ms <= 8.0` 断言超预算 |
| `Showcase_SteadyStateTickBudget_MinimapProjectionAndAttributeAggregation` | 性能预算 | `projectionMean <= 0.6` 断言超预算 |
| `GenreInfoShowcase_PlayableAcceptance_WritesLocalizedScreensAndArtifacts` | showcase 验收 | 断言失败 |
| `MapLoad_SubmitsThirtyThousandInstancesIntoRaylibIsmBuckets` | showcase 验收 | 断言失败 |
| `MinimapMarkerLargeWorldShowcase_SubmitsVisibleWorldSpheres` | showcase 验收 | `sphereRows=0, orientationRows=0`，期望可见 minimap marker 球体行 |
| `PlayableLifecycle_UsesMemberExecutionContractsAndUnloadsCleanly` | 生命周期 | 断言失败 |
| `ProjectionMap_BootstrapsPresenterInstances_AndEmitsWorldPrimitives` | ProjectionMap 族 | 断言失败 |
| `ProjectionMap_CameraFixture_DisablesEntityHudPresenters` | ProjectionMap 族 | 断言失败 |
| `ProjectionMap_PresenterSnapshot_RebuildsPerFrameWithoutRetainingDestroyedOwners` | ProjectionMap 族 | 断言失败 |
| `ProjectionMap_WritesPresenterLaneAcceptanceArtifacts` | ProjectionMap 族 | 断言失败 |
| `Showcase_MouseBoxAcquisition_RightClickIssuesOrdersForCommandableAgents` | 输入/下令 | `IsControllableBy(localPlayer, entity)` 期望 True |

原始日志：本目录 `PresentationTests.detail.log`、`PresentationTests.run.log`、`UiShowcaseTests.run.log`、`RaylibAdapterTests.detail.log`、`ArchitectureTests.run.log`。

## CI 侧背景

`solution-verify.yml` 只跑白名单切片（GasTests / PresentationTests 全量在 origin/main 上已知红，workflow 注释明示）。本 PR 新增测试打 `ci-gate` 类别进白名单；上述 13 个既有失败不在 CI 白名单内，不构成本 PR 的 CI 阻塞。
