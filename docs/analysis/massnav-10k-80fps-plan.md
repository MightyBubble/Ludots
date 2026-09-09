# MassNavigation 10K → 80 FPS 目标分析（工作稿）

基线：`origin/main` = `85dfe3529f`（**已含 PR #1485 全量**，合并点 `ffd11358f7`）。
工作树：`.worktrees/pi-1485-perf`，分支 `perf/massnav-10k-80fps`。
载体：`launcher.mass-navigation-10k-hud.runtime.json`（10K agents + AgentBridgeMod，Release）。

## 0. 结论先行

- **PR #1485 已经合进 main**（2026-09-09），无需再合；它的地形/HUD/骨骼修复是后续一切测量的起点。
- **PR #1463（GAS 效果事务）的实现、规模回归测试、评审文档全部已在 main**（`TransactionEntityIndex.cs` / `EffectPhaseSideEffectTransaction.cs` / `EffectLifetimeScaleTests.cs` / `EffectTransactionIndexTests.cs` 与 main 逐字节无差异，分支相对 main 只剩自己的旧基线和非 GAS 文件的滞后）——**它是完全重复的 PR，应关闭**。
- 10K 整机帧率**不是 GPU 绑**：`gpuSkinBuild/gpuSkinDraw ≈ 0.4ms`，瓶颈是 CPU `tick`（14–20ms）里的 presentation + massnav sim。
- 本轮已落两个确定性优化（见 §3），求解器 step 在隔离基准里 **-22%~-31%**。
- 要稳定 80 FPS（12.5ms 帧预算）还需要 §4 的结构性动作；它们会改变展示语义，需要产品拍板。

## 1. 现场数据（Release，诊断日志）

| 指标 | main 基线 | 本轮优化后 |
|---|---|---|
| 最好帧 | 28.76ms（34.8 FPS） | 18.89ms（53 FPS） |
| 典型 tick | 13–22ms | 10–16ms |
| `presentation` | 13–20ms | 10–15ms |
| `sim`（稳态） | 0.2–4ms + 峰值 27ms | 0.3ms + 峰值 2.6ms |
| GPU 蒙皮 | 0.4ms | 0.4ms |

单帧 CPU 预算拆分（典型）：

| 系统 | main | 优化后 |
|---|---|---|
| CameraCullingSystem | 3.6–6.2ms | 2.3–4.7ms |
| MinimapPresentationSystem | 2.3–4.5ms | 2.2–4.5ms |
| PresenterEntityTransformSyncSystem | 1.4–7.9ms | 1.8–7.5ms |
| PresenterEmitSystem | 1.3–5.4ms | 1.0–4.8ms |
| WorldHudToScreenSystem | 1.3–2.5ms | 1.3–2.5ms |
| MassNavigationSimulationStepSystem | 0.3–37ms | 0.3–18.8ms |

## 2. 根因清单

### P0-1 MassNav 求解器 steering + hard resolve（已优化，见 §3）
- 硬解析每对候选**探测两次**（`AreAgentsPenetrating` 后又 `SeparateAgents` 重算 dx/dy/半径）。
- 分离力每对穿透邻居都走 `ResolvePair*` 两次 + `ComputeSeparationResponse`（含 relation 矩阵查找、policy 分支、两处 `MathF.Max`、clamp）。
- 热路径反复解引用 `Semantics.Solver` / `AvoidanceTuning` 类属性。

### P0-2 CameraCullingSystem 每帧全量（已部分优化）
- `cullSpatial` 1.1–3.6ms：每帧 `_spatial.QueryAabb` + **65K `HashSet<Entity>` 清空/回填/查成员**。
- `cullDyn` 0.4–2.6ms：动态实体每帧全量 `ProcessEntity`（spatial gate + chunk gate + AABB/覆盖率 + LOD）。
- 结构债：**40 个 `QueryDescription` + 20 个近乎复制的 `ProcessStatic*/ProcessVisual*`**；`ProcessNoVisual` 还逐实体 `World.TryGet` 取本可用 chunk span 的组件。

### P1-1 MinimapPresentationSystem 每帧全量重投影（未优化，需产品拍板）
- `MinimapRuntime.ProjectMarkers` 每帧遍历全部 10009 marker：投影 + orientation bucket + style key + `TryStageBucketKeyed`（**`minimapProject` 2.5–3.4ms**）。
- 小地图 field 只有 272–660px，10000 marker 在 272² 上是 **~7.4 个/像素**；屏幕绘制已由 `SkiaOverlayRenderer.DrawMinimapMarkersBatched` 按 bucket 批量化 + sprite 缓存（所以 `overlayPaint` 不是每 marker 成本，降 marker 数对它收益有限）。
- 无 revision 闸门，也无像素级去重/密度上限。**按像素 cell 去重能省下 `minimapProject` 的绝大部分**，但它会改 `VisibleMarkerCount` 语义，而 `MinimapShowcaseAcceptanceTests` / `MinimapKnowledgeProjectionTests` 正在断言精确值（20/3/2/1）——属于产品级语义变更，本轮未动。

### P1-2 PresenterEmitSystem / TransformSync 每帧全量 30K presenter
- `EmitQuery` 每帧迭代全部动态 presenter（约 20–30K），逐条 `ResolveCachedDefinition` + 快路径/慢路径。
- `PerfHasEmitWork` 标记的增删是 **Arch 结构变更**（archetype 迁移），10K 可见性抖动时会连续触发。

### P2 main 上 `worldHud=20000`
- 与 PR head 的 4898/8246 不同；实为运行时 agent 存活/在屏差异，不是回归（遮挡缓存不改变可见性结论）。

## 3. 本轮已落改动

### 3.1 `perf(massnav): fuse penetration probe and hoist separation tuning`（`f9b73dea3d`）
`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs`
- 硬解析把「探测 + 分离」合成一次：dx/dy/d2/半径只算一次并传入 `SeparateAgents`。
- 分离循环内联 `ComputeSeparationResponseInline`，把 tuning 标量、`minNavMass`、`bodyRadius`、`navMass`、teamCount 提到 per-agent 循环外；`MathF.Max/clamp` 改条件表达式。
- 删除因此失效的 `ResolvePairBodyRadiusSumCm` / `ResolvePairSeparationRadiusCm` / `ResolvePairHardCandidateDistanceCm` / `AreAgentsPenetrating`。

隔离基准（`MassNavigationSolverBenchmark`，min-of-120，同机交替 A/B）：

| 布局 | before | after | Δ |
|---|---|---|---|
| dense-orbit 10K | 4.38ms | 2.94ms | **-33%** |
| dense-orbit 20K | 8.88ms | 6.66ms | **-25%** |
| opposed-march 10K | 6.63ms | 3.51ms | **-47%** |

其中 steer 3.34→2.05ms、hard 0.94→0.82ms（10K dense）。

### 3.2 `perf(culling): replace spatial-candidate HashSet with an entity-id stamp lane`（`6b136c79db`）
`src/Core/Systems/CameraCullingSystem.cs`
- `HashSet<Entity> _spatialCandidates` → `int[] _spatialCandidateStamps`（`entity.Id + 1` 索引 + 帧戳）。
- 65K HashSet 的 Clear/Add/Contains 全部换成数组写读；每帧省一次大表哈希。

### 3.3 验证
- `ThreeCTests`：Culling 17/17 通过；全量 112/119，7 个失败为 main 基线既有的 `CameraShowcaseMod_*`（已 stash 对照确认）。
- `PresentationTests` MassNavigation 123/125；2 个失败为 **flaky**（`Showcase_MouseBoxAcquisition_*`，main 上 3 次跑 2 次失败，PR #1485 正文亦声明基线失败）。
- 隔离基准：`MassNavigationSolverBenchmark`（`[Explicit]`，不进 CI）。

## 4. 未做 / 需产品拍板的动作（按预期收益排序）

1. **Minimap 按像素 cell 去重**（预估省 2–3ms，集中在 `minimapProject`）：每个 field 像素至多一个代表 marker。会改 `VisibleMarkerCount` 语义，`MinimapShowcaseAcceptanceTests`（断言 20）与 `MinimapKnowledgeProjectionTests`（断言 3/2/1）需同步改口径。屏幕绘制已批量，故对 `overlayPaint` 收益有限。
2. **Health HUD/Text 的 `maxLod`**（预估省 3–7ms）：配置已支持 `maxLod`（`PresenterDefinitionConfigLoader`），但全仓无人使用。给 `mass_navigation_agent_health_*` 设 `maxLod: Medium` 可让远处 80% 的血条/数字停止 emit。直接改变 showcase 视觉。
3. **PresenterEmit 从「每帧全量迭代」改为真 dirty 驱动**（预估省 3–6ms）：现在靠 `PerfHasEmitWork` 结构标记 + 每帧全量扫；需把可见性抖动改成非结构写入（如帧戳 lane），否则结构变更本身更贵。
4. **CameraCullingSystem 去重**（收益 1–3ms + 可维护性）：40 个 QueryDescription / 20 个复制方法收敛成带策略参数的少数循环；`ProcessNoVisual` 走 chunk span。
5. **`MapEntityLifecycleObserverSystem` 每帧全量 diff**（0.6–1.5ms）：`CollectCurrentMembers` 全量重建 10K HashSet + `PreviousMembers.Contains` 逐项；可改为增量 spawn/death 事件驱动。

## 5. 顺带发现的 main 基线问题

- **`GasTests` 在干净 main 上编译失败**（8 个 CS0119，均在 `src/Tests/GasTests/GasCore/DeferredTriggerTests.cs:42-57`）：`using static NUnit.Framework.Assert` 让 `Throws.TypeOf<...>()` 被解释为 `Assert.Throws(...)` 方法组。这是基线断链，不是本轮引入；它会让任何依赖该项目的 PR 验证链路失真，建议单独修。
- `ThreeCTests` 的 7 个 `CameraShowcaseMod_*` / `CameraAcceptanceMod_*` 失败在干净 main 上同样存在。
- `Showcase_MouseBoxAcquisition_AcquiresVisibleMassNavigationAgents` **flaky**（main 上 3 次跑 2 次失败）；`RightClickIssuesOrders...` 稳定失败，与 PR #1485 正文声明一致（下单链另有缺陷）。

## 6. 远端/本地待整合项盘点

| 分支 / PR | 状态 | 与 10K 帧率的关系 | 建议 |
|---|---|---|---|
| #1485 `fix/hud-dirty-projection` | **已合入 main** | 地形/HUD/骨骼修复的起点 | 无需动作 |
| #1463 `codex/effect-transaction-performance` | OPEN，但**实现 + 测试 + 文档全部已在 main** | `TransactionEntityIndex.cs` / `EffectPhaseSideEffectTransaction.cs` / `EffectLifetimeScaleTests.cs` / `EffectTransactionIndexTests.cs` 与 main 逐字节无差异 | **关闭（完全重复）** |
| #1488 `codex/entity-attachment-motion-contract` | OPEN | 改的是 GAS `AttachmentPositionSyncSystem`，非 presenter 热路径 | 与本目标无关 |
| #1486 `codex/presenter-attachment-contract-perf` | OPEN | presenter attachment 契约重构（+19.8K 行） | 风险大，建议独立评审 |
| #1470 `codex/presenter-retained-visibility` | OPEN | presenter 可见性/清理 | 可独立评审，非本轮 |
| #1484 `codex/navmesh-board-addressing-m1` | OPEN | navmesh 每板寻址 | 与本目标无关 |
| `codex/nav-perf-query-cache-rebake-workers` | 本地远端分支 | 烘焙并行 + Detour mesh 共享 | 与本目标无关（烘焙期，非每帧） |
| `cursor/massnav-drop-visual-scale-77d9` (#1287) | OPEN | 去掉 solver 里的 visualScale | 语义清理，非帧率 |

---

## 7. 30K 静态 presenter 场景横向对比（用户点名的「六月 60 FPS」）

用户的判断成立。同一台机、同一个 `CapabilityStandardStaticPresenter30kMod`、同一个
`capability_standard_static_presenter_30k_scatter_hudtext_benchmark` 地图
（30K presenter + 30K 血条 + 30K 文本，`scatterInitialTarget=30000`）：

| 指标 | JUN16 (`2f6a8afb1f`) | 当前 main（修复配置后） |
|---|---|---|
| frame | **9.81–11.46ms（87–102 FPS）** | 28.3–32.3ms（31–35 FPS） |
| tick | 2.85–3.23ms | 4.9–7.2ms |
| **`cullStatic`** | **0.00–0.02ms** | **2.1–5.4ms** |
| `overlayPaint` | 4.4–6.2ms | 0.00ms |
| `worldHud` | **60000** | **0（未发射）** |

### 7.1 修复：30K mod 在 main 上完全渲染不出来（已修）

在修之前，`CapabilityStandardStaticPresenter30kMod` 在 main 上 `visibleEntities=0`、
`primitiveRaw=0` —— **一片黑**。三个叠加的契约缺口：

1. **缺 `startupLocalSeats`** → 无 PresentBinding → `RaylibHostLoop` 调用的
   `DisarmPresentBindingCulling()` 永不被 `TryArmPresentBindingCullingPasses` 反向解除
   → `CameraCullingSystem.Update` 直接 return → 所有实体 `CullState.IsVisible` 保持 false。
2. **缺 `Players` / `Teams` / 代表实体**：光加 `startupLocalSeats` 会在
   `ParticipantBindingResolver.ResolveLocalSeats` 抛
   `playerId 1 references an unbound player`。参考基座 `MassNavigationMod` 的
   `mass_navigation` 地图，需要 `Players[].RepresentativeInstanceId` +
   `Teams[].RepresentativeInstanceId` + 两个代表实体。
3. **缺 `gasRuntimeCapacity.effectFanOutCommandCapacity`**：30K 出生瞬间
   `EffectApplicationSystem` 的 `PendingEffects` 固定容量溢出并 fail-closed 抛
   `GAS.EFFECT_APPLICATION.ERR.FixedListCapacityExceeded: list=PendingEffects, capacity=16384`
   （该容量由 `effectFanOutCommandCapacity` 驱动，与 `effectRequestQueueCapacity` 无关）。

修复落在 30K mod 的数据层：`templates.json` 补两个代表模板；八个地图各补
`Entities` 代表 + `Teams` + `Players`；`game.json` 补 `effectFanOutCommandCapacity=131072`
与 `startupLocalSeats`。修复后 `visibleEntities=30000` / `primitiveRaw=30000`。

### 7.2 仍开着的两个 30K 回归

- **`cullStatic` 2–5ms/帧**：相机每帧变化时 `runFullStatic=true`，对 30K 静态实体做
  全量 `ProcessStaticEntitiesFull`（AABB + 视口 + LOD）。JUN16 同场景 `cullStatic=0.00ms`
  —— 六月在相机动时才重算，且当时这条路径几乎没有成本。需要按「相机位移后仍是同一 LOD/
  可见集」做增量，或把静态实体的 AABB/LOD 预算缓存到块级。
- **`worldHud=0`**：`capability_static_presenter_mesh_benchmark_hudtext` 的 WorldHud
  行为在 main 上不发射（JUN16 是 60000 条）。这是独立于容量的第二处表现层回归。

> 说明：本轮没有改这两个（前者要动 culling 算法与语义，后者根因未定位到具体契约），
> 只把它登记为下一步的确定目标，避免在没定位到根因前瞎改。
