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

---

## 8. 30K 静态场景的瓶颈：ISM 片元着色器（用户「不画 HUD 也卡」的确切答案）

### 8.1 先排除的几项

| 假设 | 实测 | 结论 |
|---|---|---|
| ISM 没走上、退化成逐实例 draw | `primInstances=30000`、`primBatches=1..2`、`primCache=1/0` | **ISM 正常**，30K 在 1 个 chunk 里（`DefaultMaxModelInstancesPerDraw=32768`） |
| HUD / Skia overlay 太重 | `worldHud=0`、`overlayItems=0`、`overlay=0.16ms` | 本场景 HUD 根本没画 |
| 分辨率 / fill 面积 | 320×180 窗口下 frame 仍 ~30ms | **不是屏幕填充率**（不是窗口大小） |
| 阴影 pass | `LUDOTS_RAYLIB_SHADOW=0`：26.5 vs 27.4 FPS | 阴影不是主因 |
| CPU 绑 | `dotnet-trace`：主线程 97% 在等，`RaylibHostLoop.Run` CPU inclusive 仅 2.64%；`endDraw` 常态 0.1ms、偶发 20–100ms | **GPU 绑**（`GPU Engine 3D` 实测 76% 利用率） |

### 8.2 真正的根因：`instancing.fs` 从 13 行变 137 行

同一份 building mesh（`building_blacksmith_blue.bin` md5 与六月一致，`1f14c394…`），
但 ISM 的片元着色器被 PBR 化：

| | JUN16 | 当前 main |
|---|---|---|
| `src/Platforms/Desktop/instancing.fs` | **13 行**（unlit `texture()` × 1） | **137 行** |
| 每片元采样 | 1 | albedo + roughness + metallic 3 张 + `SampleShadow` PCF 5 次 + `textureLod(uPrefilteredEnv, …)` 立方图 + `uBrdfLut` 2 分量 |
| 每片元算术 | 乘一下 | Cook-Torrance D/G/F + split-sum IBL + 雾 |

规模线性验证（同一场景，只改 `AUTO_SCATTER_TOTAL`）：

| instances | FPS |
|---|---|
| 3,000 | 187–242 |
| 10,000 | 68–100 |
| 30,000 | **20–30** |

**随实例数线性下降**，且与窗口尺寸无关 ⇒ **重叠建筑把 PBR/IBL 片元成本乘了 30K 遍**。
这就是「不画 HUD 也卡」和「ISM 像没用上」的实际成因：ISM 提交没问题，
**是每个实例的片元成本从 unlit 变成了完整 PBR**。

### 8.3 修复方向（未实施，需产品拍板）

1. **按 LOD 档切 shader**：`cameraCulling` 已有 High/Medium/Low（本场景 20000/70000/180000cm）。
   让 Low 档走回 unlit/简化 lit 着色器，或在 `instancing.fs` 里按 `uQualityTier` 提前 `return`
   掉 IBL/PCF 分支（片元级分支，比统一砍画质更安全）。
2. **IBL 全关了再测**：`uEnvSpecular` 已经是 1.0 常量，可以做成配置/环境开关先验证收益上限
   （预期省掉立方图 `textureLod` + BRDF LUT 两次采样）。
3. **不要用 `maxLod` 削 HUD**（上一轮的误判）：本场景 HUD 是 0，砍 HUD 无用；
   真正要砍的是**远处实例的片元着色质量**，不是 HUD 条目。

---

## 9. 两条渲染路径都吃同一套 PBR shader —— skinned 也受影响，但瓶颈不同

用户问「skinned mesh instance 是不是也受影响」。**是**，但两条路是**不同的瓶颈**，
用 320×180 窗口（1/25 像素）做了判别性实验：

| 场景 | 1600×900 | 320×180 | 判别 |
|---|---|---|---|
| 30K 静态 ISM（建筑） | 20–30 FPS | ~30 FPS（同） | 与分辨率无关 ⇒ **片元/填充**（重叠建筑）|
| 10K massnav skinned（士兵） | 18–25 FPS | 18–25 FPS（**几乎不变**） | **与分辨率无关** ⇒ **顶点/几何** |

### 9.1 片元侧：两个 FS 的照明代码逐字节相同

`diff <(instancing.fs) <(skinning_instanced.fs)`（去空白）只差 tint/fragColor 的接线，
照明段完全一致。每片元固定成本：

- `texture0` 反照率 + 可选 `texture3` 粗糙度 + 可选 `texture1` 金属度
- **`SampleShadow` = 3×3 共 9 次 shadow map 采样**（`shadow_sampling.glsl.inc`，由 `uShadowEnabled` 动态分支门控）
- `textureLod(uPrefilteredEnv, …)` 立方图 1 次 + `uBrdfLut` 1 次
- Cook-Torrance D/G/F + split-sum IBL + 距离雾

⇒ 最坏每片元 **14 次纹理采样**。JUN16 的 `instancing.fs` 是 **1 次**。

### 9.2 顶点侧：skinned VS 每顶点最多 18 次取骨

`skinning_instanced_pose_texture.vs` 每顶点：
- 实例表 `texelFetch` × 2（poseRow + tint/alpha）
- 最多 4 个权重分支，每个 `FetchBoneMatrix` 内部 **4 次 `texelFetch`** ⇒ 最多 **16 次**

士兵模型 `mass_navigation_agent_soldier.glb` = **6,952 tri / 15 mesh part**
（与 `Knight.glb` 同模型，md5 一致）。10K 实例 = **≈6,950 万三角面/帧** × 每顶点 ≤18 次
纹理取骨 ⇒ 每帧上亿次 texel fetch。**这与分辨率无关**，正是 320×180 不掉帧的原因。

### 9.3 LOD 已经算出来了，但渲染器根本没用

`CameraCullingSystem` 每帧算出 `CullState.LOD ∈ {High, Medium, Low}` 并经
`presentation.cameraCulling` 配了阈值（30K 场景 20000/70000/180000cm），
但 `RaylibPrimitiveRenderer` / `RaylibFrameRenderer` **没有任何读取 LOD 选 shader 或材质的代码**。
⇒ 远近一视同仁，全走完整 PBR/IBL。

### 9.4 修复方案（按风险从低到高）

1. **给 FS 加质量档 uniform（最小改动、可回退）**
   两个 FS 各加 `uniform int uQualityTier`：`2`=全量，`1`=跳过 IBL（省立方图 + BRDF LUT 2 次采样），
   `0`=退化为 `albedo × (环境漫反射 + NdotL × 光)`（省掉 Cook-Torrance / 9 次 PCF）。
   由 C# 侧按 `CullState.LOD` 直接设。同一 shader、同一个 draw call，只多一次 uniform 写。
2. **把 LOD 接通到渲染器**：`SkinnedVisualBatchItem` / ISM bucket 需要携带 LOD，
   或按 LOD 分桶（`primBatches` 从 1 变 3，可接受）。
3. **skinned 侧另加顶点降级**：远处改成 2 权重（`skin` 只取 2 个 bone），或
   按 LOD 用不同 mesh（若模型有 low-poly 变体）。这一条改动最大，放到第二期。

> 先做 1（尤其 FS 质量档），因为它同时覆盖 ISM 与 skinned 的片元成本，
> 且不动数据契约；skinned 的顶点取骨降级作为第二期，需要单独验收不许劣化骨骼姿态。

---

## 10. 实施记录：culling 修复落地，片元质量档试验后回退

### 10.1 已落地：`fix(culling)` —— 每帧 rebind 抹掉静态 cull 缓存（`da4078076e`）

**根因**：`RebindPipeline`（`PresentBindingPresentation.cs:506` → `RebindPresentBinding`）
与 `TryArmPresentBindingCullingPasses`（→ `RebindPresentBindings`）
**每帧 tick 前都调一次**，且每次都 `new PresentBindingSurface(binding, fov)`。
两条路都无条件走 `ResetPassStaticCaches()` → `_passHasStaticCameraState` 被清空
→ `runFullStatic = !hasStaticCullCameraState` **恒为真** → 每帧全量重扫所有静态实体。

三处坑叠在一起才让它一直不收敛：
1. `PresentBindingSurface` 是 host 每帧 new 的，`ReferenceEquals` 永不成立；
2. `PresentBinding` 是 `readonly struct`，我第一版用 `ReferenceEquals(Binding, Binding)` 装箱比较，也永不成立；
3. 单绑定路径 `SeatId=null`、复数路径 `SeatId=seat.0`，两个入口互相判不等，交替清缓存。

**修法**：等价 pass 集直接 no-op —— camera 按引用、surface 按
`Binding.Equals` + `Fov` 比较，`SeatId` 只作描述不作判据。

**实测（30K 静态场景，同一台机）**：

| 指标 | 修前 | 修后 |
|---|---|---|
| `cullStatic` | 5.0–8.7ms | **0.00ms** |
| `tick` | 6.6–15.4ms | **1.6–2.1ms** |
| `cull` | 4–8ms | **0.01–0.04ms** |
| GPU 3D 利用率 | — | **85.8%（纯 GPU 绑）** |

10K massnav 同修受益：最好帧从 ~35 → **54 FPS**，修后 GPU 94%（转为纯 GPU 绑）。

### 10.2 已回退试验：FS 片元质量档 `uQualityTier`（`dc40e9f388` → revert `cd1b9c70dc`）

试过在 `instancing.fs` / `skinning_instanced.fs` 加 `uniform int uQualityTier`
（0=unlit 级 / 1=无 IBL / 2=完整 PBR）。**结论：这条路子不成立，已回退。**

- ISM 侧：tier 0 确实把 GPU 3D 利用率从 ~83% 压到 ~49%，但 FPS 不动
  —— 当时 CPU 还没修，`tick` 兜底。修完 cull 后再测，30K 仍卡在 11–30 FPS，
  因为 **tier 0 依然要光栅化 7,200 万三角面**（`frame=89ms` 里 `endDraw=34.6ms` 是 present 等待）。
- **skinned 侧：tier 0 反而更慢 3 倍**（28 → 9 FPS，`mode3D` 1.9 → 9.5ms）。
  原因是运行时 `if (uQualityTier < 2) return;` **并没有把 tier-2 的代码从编译产物里去掉**，
  寄存器压力反而升高、占用率下降；skinned 本身已经顶点很重，雪上加霜。

⇒ 运行时分支的"质量档"是错工具。若要做，必须走**独立编译的轻量 program**
（`instancing_unlit.fs` 等）按 LOD 选程序，而不是同一 program 加 uniform 分支。

### 10.3 下一步（按实测优先级重排）

| # | 动作 | 预期收益 | 依据 |
|---|---|---|---|
| 1 | **阴影距离裁剪**：现在 30K 全进阴影 pass（`primBatches=2`），实测 `SHADOW=1/0` GPU 57% vs 39% | ~18 点 GPU | §10.1 已确认纯 GPU 绑 |
| 2 | **低模 LOD mesh**（30K × 2,410 tri = 72M tri/帧 是几何量瓶颈，与分辨率无关） | 直接砍几何 | §8/§9 320×180 实验 |
| 3 | 独立轻量 shader program 按 LOD 选（不是 runtime 分支） | 省片元 | §10.2 |
| 4 | skinned 顶点取骨降权（远处 2 权重） | 省顶点 | §9.2 |

---

## 11. 本轮落地清单（按提交顺序）与最终实测

| 提交 | 内容 | 实测 |
|---|---|---|
| `283c6afdad` | 30K mod 在 main 上渲染不出来（三层契约缺口） | `visibleEntities` 0 → 30000 |
| `6b136c79db` | culling 空间候选 HashSet → entity-id 帧戳数组 | 每帧省一次 65K 哈希表 |
| `f9b73dea3d` | massnav 硬解析探测/分离合并 + 分离调参外提 | 求解器 step −22%~−33% |
| `da4078076e` | **每帧 rebind 抹掉静态 cull 缓存**（真 bug） | `cullStatic` 5.0–8.7ms → **0.00ms**，`tick` 6.6–15.4 → 1.6–2.1ms |
| `7c366ab0f2` | 阴影投影体外的 caster 裁剪 + 恢复 10K 窗口 1600×900 | 10K 每帧裁掉 2.4K–4.2K skinned caster |
| `c407bd7d93` | **flow 避障从「每格扫 9×9」改为「稀疏阻塞格索引」** | flow 重建 11.5ms → **1.15ms** |

### 最终实测（Release，1600×900，同一台机）

| 场景/指标 | 本轮开始 | 本轮结束 |
|---|---|---|
| 10K massnav 最好帧 | ~35 FPS | **56.1 FPS** |
| 10K `cullStatic` | 5.0–8.7ms | **0.00–0.01ms** |
| 10K flow 重建 | 9.0–11.5ms | **0–1.15ms** |
| 10K 求解器基准（dense-orbit 10K） | 2.94ms | **2.50ms** |
| 30K mod 可见实体 | 0（一片黑） | **30000** |
| 30K `cullStatic` | 5.0–8.7ms | **0.00ms** |

### 当前剩余瓶颈（已定位、未动）

1. **`MassNavigationSimulationStepSystem` steering 4–8.7ms + hard 0–4.4ms**：密度决定（100cm 网格里约 1 agent/格，每 agent 扫 5×5 格 ≈ 25 邻居）。且 `hardPenetrating` 长期占 18K–31K/88K–145K 对 → **分离不收敛是仿真质量问题，不只是性能**。
2. **`CameraCullingSystem` 2.4–4.8ms**：动态实体每帧全量 `ProcessEntity`。
3. **`MinimapPresentationSystem` 2.3–4.3ms**：每帧对 10K marker 做 knowledge 解析（viewer 已注册，缓存按 owner 命中不了，因每 marker 一个 owner）。
4. **30K 场景是纯 GPU 绑（85%+）**：30K × 2,410 tri = 7,200 万三角面/帧；除非换低模，否则是硬地板。

---

## 12. 与 origin/main 合并（`3113ae3cde` + `63583b5dd3`）

合入前 main 已前进 6 个提交，且**打的正是我列出的同一批热点**：

| main 提交 | 内容 | 与我的关系 |
|---|---|---|
| `5657c69fbf` | hard resolve 每 agent 每 pass 分离预算 + `QuadrantSpread` 出生散开 | **互补**（我列的第 1 项，main 先做了） |
| `de3dddd1bb` | `QuadrantSpread` 逐单位散点目标 | 互补 |
| `f1a971a3d7` | 预算字段挪回 `semantics.solver`（修真机启动崩溃） | 无关 |
| `7ecd469645` | GAS 出生种 tag 快照，消结构性补件 | 互补（我提到的「热路径结构变更」） |
| `1699990795` | 无消费者时跳过地图事件分发 | 互补（分配 −18%） |

**冲突只有 1 处**（`MassNavigationFlowSolverState.cs` 的 hard-resolve 内层）：保留我把已算好的
`dx/dy/d2/半径和` 直接传给 `SeparateAgents` 的融合形式，叠加 main 的每 agent 分离预算 —— 预算路径
不会退回「探测两遍」。

### 合并后实测（headless 10K，1600×900）

| 指标 | 数值 |
|---|---|
| main 的分离预算效果 | `hardPenetrating` 峰值 52K → 稳态 ~2K；`hard` 5.4 → ~1.8ms |
| main 的出生散开效果 | `hardPairs` 起始 100K+ → 7.8K |
| 我的 flow 索引 | flow 重建 **0–1.4ms**（合并前后一致） |
| 我的 culling 修复 | `cullStatic` **0.00ms**，`visibleEntities=10006` 正常渲染 |
| 稳态 `sim` | **0.19–7.6ms**（此前 20ms+） |
| 最好帧 | **49.1 FPS**（连续 5 帧 40–49） |
| 现在的主导成本 | `presentation` 12–16ms（`Minimap` 2–3、`Emit` 2–5、`TransformSync` 1–4、`cull` 2–3.7） |

### 合并中我自己引入并修掉的一个回归（`63583b5dd3`）

合并后 10K 只剩 `visibleEntities=5`、`gpuSkinned=0`（单位不渲染）。定位为我上一轮的 culling 优化
`RebindPresentBinding` 提前 return —— host 每帧都新建 `PresentBindingSurface` 包装，提前 return 会让
缓存里的包装**陈旧**，可见性判定失效。修法：**pass 条目始终重写**（保证包装是最新的），只把
`ResetPassStaticCaches` 继续按「相机绑定是否真的变了」门控。修后 10006 可见 + `cullStatic=0` 同时成立，
`ThreeCTests` 回到既有的 7 失败 / 112 通过。

> 教训记在案：cull 缓存这类「跳过重算」的优化，必须把**旁路的状态刷新**和**缓存失效**分开处理，
> 不能整体 early-return。

---

## 13. PR #1486 评估结论：**暂不合，但有 1 个可单独摘的优化（已试，收益不成立）**

### 13.1 为什么不能整体合

`codex/presenter-attachment-contract-perf`：**267 文件 / +20137 −1769**，其中
**235 个是 artifacts 证据**，真正的代码是 `src/` 下 **32 文件 / +2136 −817**。

实测动作：
1. `git merge origin/codex/presenter-attachment-contract-perf` → **无冲突**，编译通过。
2. 跑 10K 载体 → **直接崩**：
   ```
   System.InvalidOperationException: Presenter behavior references unknown Mesh asset 'mass_navigation.command.marker'.
   ```
   该 PR 往 `MassNavigationMod/assets/Presentation/presenters.json` 加了
   `mass_navigation_agent_command_marker_{light,heavy}` 两个定义，引用 Mesh 资产
   `mass_navigation.command.marker` —— **该资产在 main 与该分支的任何 `mesh_assets.json` 里都不存在**
   （`git grep` 全仓零命中；MassNavigationMod 的 `mesh_assets.json` 只有 3 条，都不匹配）。
   ⇒ 这个 PR 自身在 massnav 场景上是**启动即失败**的，与它 Draft + 7 条未勾选合入阻塞项的状态一致。

结论：**不能整包合**。它自己也承认「旧隐式空间行为的完整迁移仍未完成」，且它的 10K 测量是
「约 3 FPS、EndDrawing 265–278ms」（未区分 GPU 执行与窗口等待，未验证恢复 60 FPS）。

### 13.2 那条收窄：**第一次判断错了，复查后成立并已合入**（`f7c7c750d3`）

该 PR 里最贴我热点的是 `4416820854 perf(presentation): restrict entity transform sync to compiled roots`
—— 只 11 行，给 `EntityAnchoredQuery` 加 `PerfEntityAnchorRootTransformSync` 标记，
把每帧变换同步从「全部 presenter」收窄到「entity-anchored 根」。
它依赖的 `IsEntityAnchoredRootPresenter(Entity)` **在 main 上已存在**，所以我不依赖该 PR，
在本分支原生实现了一遍。

**第一次测量得出的「收窄会把挂接子对象同步漏掉」是错的** —— 那次测量时 PR #1486 的整包 merge
还留在工作树里，而它带来的 `mass_navigation.command.marker` 资产缺失正在污染结果。
清掉 merge 后重新做同条件 A/B：

仪器化先确认了一件事：10K massnav **根本不走**这条 entity-anchored 循环
（`EntityAnchoredQuery` 每帧命中 0 个实体；日志 600 帧全是 `queried=0`）。
根走的是 owner-payload 路径（`ownerChunks=102`、`ownerChanged` 在 0 / 9717 之间交替，
即 15Hz 仿真 vs 60Hz 渲染的节奏）。所以这条收窄在**本场景是低风险的结构清理**，
收益来自「少扫一遍带 `PerfTransformSyncTick` 的全部 presenter」。

同条件 A/B（10K massnav，1600×900，各 31 样本）：

| 指标 | 基线 | 收窄后 |
|---|---|---|
| `transformSync` 均值 | 2.66ms | **2.10ms** |
| `transformSync` 峰值 | 8.04ms | **6.43ms** |
| `visibleEntities` | 10006（稳定） | 10006（稳定） |
| `worldHud` | 20000（稳定） | 20000（稳定） |

回归验证：`PresentationTests` 有/无该改动都是 **574 通过 / 10 预存在失败**，逐一致；
`ThreeCTests` 112/119（7 个既有）。最终 26/26 样本稳定在 `10006` / `20000`。

> 教训：**A/B 必须在干净基线上做**。中途留着一个尚未验证的第三方 merge（哪怕它编译通过），
> 会把它的缺陷记到自己的改动账上。第一次结论因此完全反了。

### 13.3 建议

- **#1486 不并入本分支**。它是契约重构，正确做法是它自己补齐阻塞项（尤其那次
  `mass_navigation.command.marker` 资产缺失是纯粹的配置错误，必须先修）后再独立评审。
- 其中有价值的**思路**（按 anchor 收窄变换同步）可以做，但前提是先把「挂接子对象的
  兜底同步」拆成独立车道，否则就是上面这个塌陷。
- 它的 artifacts 目录（235 文件）不应随代码一起进主干。
