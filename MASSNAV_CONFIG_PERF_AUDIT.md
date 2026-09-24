# MassNavigation / NavMesh 配置与运行时性能审计（续）— 修正版

审计基线：`.worktrees/massnav-audit`（`81a1b5f543`，origin/main）。
范围：`src/Core/MassNavigation`、`src/Core/Navigation/NavMesh`、`src/Core/Navigation/Pathing`、`src/Core/Ludots.Physics2D/Navigation`，以及全部相关 JSON 配置资产。

> **重要更正说明**：本版本修正了上一稿中的三处误判（M-C1 / M-D2 / M-N1）。这些误判是在只做静态比对、未实际运行/验证的情况下得出的。本次已通过运行测试与逐文件核对逐一修正，并额外发现并修复了真正的 P0 阻断（右键下单指令链）。详见文末"修订记录"。

---

## 第一部分：已确认的配置 / 性能问题（经验证）

### K-P1：road_network 的 `flowCellSizeCm` 静默继承，导致 500×500 流场网格

`RoadNetworkShowcaseMod/assets/MassNavigationConfig.json` 的 `solver` 只声明了 `fieldWidthCm=50000`、`fieldHeightCm=50000`、`parallelWorkerCount=1`、`playAreaMaxXCm/Zcm=49950`，**没有声明 `flowCellSizeCm`**。

`MassNavigationConfig.json` 通过 `DeepObject` 深度合并（`ConfigMerger.MergeDeepObject` → `ConfigPipeline.DeepMerge`，嵌套对象递归合并、标量覆盖）。于是 base `MassNavigationMod` 的 `flowCellSizeCm=100` 被继承，而 `fieldWidthCm` 被覆盖为 50000。

计算结果：
- 网格 = `50000 / 100 = 500 × 500 = 250,000` 格；
- 4 个 flow state × 2 个 float × 250,000 = 2,000,000 float ≈ **7.6MB 常驻**；
- `RequireGridCapacity`（`MassNavigationFlowSolverConfig.Validate`）只校验 `gridWidth × gridHeight <= int.MaxValue`，250k 远低于上限，**所以静默通过校验**。

影响：road_network 的地图是 50km 级，但流场沿用 100cm 网格语义（10K 战术地图的密度）。`flowCellSizeCm` 与 `fieldWidthCm` 之间没有任何"网格尺寸合理性"校验。若作者本意是 500cm 级网格（`50000/500 = 100×100`），当前配置会白白分配 25 倍内存和重建成本。

证据：
- `mods/showcases/road_network/RoadNetworkShowcaseMod/assets/MassNavigationConfig.json`
- `src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`（`MassNavigationFlowSolverConfig.Validate`）
- `src/Core/Config/ConfigMerger.cs`（`DeepObject` 递归合并）

修复方向：`MassNavigationFlowSolverConfig.Validate` 增加 `gridWidth × gridHeight` 的合理上界校验（按工作区面积/期望网格密度），或在 road_network 显式声明 `flowCellSizeCm`。（注：road_network 的 `autoSpawnConfiguredScenario=false`，其 solver 实际是否走 flow 网格取决于实现——需进一步确认其是否真正使用流场。）

### K-P2：`IsObstacle` / `RebuildStaticObstacleCost` 为 O(grid × obstacle) 无空间加速

`MassNavigationFlowSolverState.IsObstacle`（`~:2757`）对每个网格格点线性扫全部障碍：

```csharp
private bool IsObstacle(float wx, float wy)
{
    for (int i = 0; i < ObstacleCount; i++) { /* 距离平方 */ }
    return false;
}
```

`RebuildStaticObstacleCost`（`~:1376`）遍历 `GridWidth × GridHeight` 格，每格调用 `IsObstacle`。10K 配置：`100×100` 格 × `maxObstacleCount=64` = **640,000 次距离计算**。且 `RequestFlowRebuild`（`MoveSolverWindow` 每次移动窗口时调用，`~:1354`）会触发 `ForceFlowRebuild` → `RebuildStaticObstacleCost`。窗口频繁移动时，这是反复的 O(grid×obstacle) 热路径。**障碍数量翻倍，成本线性翻倍；没有增量 dirty 追踪或空间索引。**

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs:2757, 1376-1385, 605-633`。

### K-P3：环境绑定每帧全量 hash + 全量重建（无增量 dirty）

`MassNavigationEnvironmentBindingSystem.Update`（`~:76`）每帧：
1. `ComputeSignature()` 全量遍历所有 blocker/marker 计算 hash（含 `Entity.Version`）；
2. 若 hash 变化 → `ClearEnvironmentCounts()` + `BindBlockers()`（全量重建）+ `BindMarkers()` + `MarkStructuralChange()`。

复合障碍若 `PieceCount` 与实体计数不匹配（此前 P1，已修），fast path 失效则每帧重建。**没有任何增量 dirty 标记机制**，只有全量 hash 比较 + 全量重灌 `MassNavigationBlockerProfile`。这是"热结构变更"的根因之一，规模越大开销越大。

证据：`src/Core/MassNavigation/Systems/MassNavigationEnvironmentBindingSystem.cs:70-190`。

### K-P4：Sonar 求解器角度硬编码 `* 0.5f`（全角/半角隐式换算）

`CreateSonarSolveConfig`（`~:2285`）：

```csharp
maxSteerAngle: SonarSolver2D.DegreesToRadians(config.MaxSteerAngleDeg) * 0.5f,
backwardPenaltyAngle: SonarSolver2D.DegreesToRadians(config.BackwardPenaltyAngleDeg) * 0.5f,
```

配置语义是"全角"（`maxSteerAngleDeg=280`），`SonarSolver2D` 内部把 `maxSteerAngle` 当"半角/张角"用（`InitializeAvailableIntervals` 中 `maxSteerAngle < PI` 时做对称截断）。`* 0.5f` 把全角换算成半角。**该换算规则未文档化，改动配置角度时极易误用**，且 `SolveConfig` 无注释。属配置语义↔实现语义的隐性耦合。

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs:2285-2286`；`src/Core/Navigation/Avoidance/SonarSolver2D.cs`（`SolveConfig` 无注释）。

### K-P5：`MassNavigationAgentState` 用管理集合（非 SoA），双索引语义漂移

`MassNavigationAgentState`：
- `_allAgents`/`_controllableAgents` 是 `List<Entity>`；
- `_spawnedEntityIds` 是 `HashSet<Entity>`、`_controllableIndexByEntityId` 是 `Dictionary<Entity,int>`。

`RegisterAgentAtIndex` 用完整 agent index 扩张 `_allAgents`（`while (_allAgents.Count <= agentIndex) _allAgents.Add(Entity.Null)`），而 `_controllableAgents` 用**紧凑计数索引**。两套并行的"agent→slot"结构靠 agent index 关联。若 agent 0 不可控、agent 1 可控，`_controllableAgents[0]` = 第一个可控对象（agent 1），而 `_allAgents[0]` = agent 0 的 Null——**两套语义不一致**（对应此前 P1）。10K 单位下，每帧 `TryGetControllableEntity`/`TryGetAgentEntity` 走托管字典/列表查找，而 `MassNavigationFlowSolverState` 本身已是平铺 float[]/int[] SoA——**ECS 边界层与 solver 层之间是两套数据结构。**

证据：`src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs:81-90, 139-164`。

---

## 第二部分：已复核并判定为"非缺陷/有意为之"的项（从上一稿撤回）

### 撤回 R-C1：CrowdPhysicsArena 的 `MassNavigationConfig.json` 是死文件 ✘（已推翻）

上一稿判断"CrowdPhysicsArena 没有 `config_catalog.json`，其 `MassNavigationConfig.json` 从未被加载，mapId 与 base 冲突导致 MassNavigation 不激活"。

**验证推翻**：`MassNavigationConfigLoader` 走 `ConfigPipeline.LoadFromAllSources`，它遍历**所有已加载 mod**，尝试从**每个 mod** 的 `assets/MassNavigationConfig.json` 加载片段——即使该 mod 没有在自身 `config_catalog.json` 注册该条目。base `MassNavigationMod` 的 catalog 注册了 `MassNavigationConfig.json`（DeepObject），于是 CrowdPhysicsArena 的片段照样被合并。

实测：`CapabilityStandardCrowdPhysicsArenaProductionPathTests.Showcase_BootsArenaWithKinematicSquadsDrivenByBridge` **通过**（运行时就绪、96 单位 spawn、kinematic feed 正确）。证明 CrowdPhysicsArena 的配置确实加载、`mapId` 确实解开、MassNavigation 确实激活。

结论：CrowdPhysicsArena 的配置**不是死配置**，该 showcase 的 MassNavigation 激活正常。撤回上一稿 M-C1。（注：该 mod 没有本地 `config_catalog.json` 是"合法"的——它依赖 base 的全局注册。唯一小不便是：该 mod 自己无法独立于 base 控制 `MassNavigationConfig.json` 的合并策略，但这与其它依赖 base 的 showcase 一致，非缺陷。）

### 撤回 R-C2：mapId / startupMapId / map Id 三源一致性 ✘（部分成立，多数是有意）

逐 mod 核对结果：

| 配置 | mapId | startupMapId | 匹配 |
|---|---|---|---|
| MassNavigationMod | mass_navigation | mass_navigation | ✓ |
| CrowdPhysicsArena | crowd_physics_arena | crowd_physics_arena | ✓ |
| ParticipantViews | *disabled_for_* | capability_standard_participant_views | ✘（**有意**） |
| EastAsiaBordersLandSea | east_asia_visual_heightmap | east_asia_visual_heightmap | ✓ |
| FormationCapability | formation_capability_showcase | formation_capability_showcase | ✓ |
| RoadNetwork | road_network_showcase_chunked | road_network_showcase_chunked | ✓ |

唯一不匹配的是 ParticipantViews——它的 `mapId` 写作 `mass_navigation_disabled_for_capability_standard_participant_views`，且该 mod **不使用任何 MassNavigation runtime API**（代码中无 `MassNavigationIds`/`RuntimeBinding` 引用）。这是一个**刻意的"禁用哨兵"**：mapId 刻意与 startupMapId 不匹配，从而让 `MassNavigationRuntime.HandleMapFocused` 对该 map 静默跳过。这是正确的设计意图，不是缺陷。

上一稿宣称"三个来源普遍不一致、错配静默跳过"是过度概括。**撤回 M-C2 的大部分**；唯一有价值的残余是：`MassNavigationConfig.Validate()` 不校验 `MapId` 与其它资产的 id 一致性，导致类似"禁用意图"依赖隐式不匹配而非显式声明——这属于可改进性，非 bug。

### 撤回 R-C3：`agentProfiles` 与 `agent_profiles.json` 无交叉验证 ✘（已存在）

上一稿说"`agentProfiles` 的 profile id 与 `Navigation/agent_profiles.json` 无强制一致"。**验证推翻**：`MassNavigationAgentProfileSetConfig.BindAgentProfiles(...).ValidateAgentProfileReferences()` 会把每个 profile id 交给 `AgentProfileRegistry.Require(...)`。`Navigation/agent_profiles.json` 通过 `AgentProfileConfigLoader` 加载进同一个 `AgentProfileRegistry`。NavMesh 侧 `navmesh.json` 的 profile 也走同一个 registry。**两边都强制 id 存在。** 所以"无强制一致"是错的。撤回。

（真正残余的只是 side note：`MassNavigationMod` 用 `light`/`heavy`，NavMesh 用 `Small`/`Medium`/`Large`——这是两个独立命名空间，各自都有 registry 兜底，不是缺陷。）

### 撤回 R-C4：M-N1 navmesh 副本 layers / maxSlopeDeg / heightScaleMeters 不一致 ✘（有意）

上一稿说"`navmesh.json` 三个副本 layers 大小写、maxSlopeDeg、heightScaleMeters 不一致是 SSOT 漂移"。**验证推翻**：这些 `navmesh.json` 是**各 mod 独立的面包配置**，分别经 `NavMeshBakeConfigLoader.LoadFromRepoRoot` 从 repo 根直接加载，**互不合并**（它们各自的 `config_catalog.json` 没有注册 `Navigation/navmesh.json`）。`Ground` vs `ground`、`55°` vs `45°`、`heightScaleMeters=0.25/2.0/1.0` 是**每个场景各自的面包参数**，不是漂移。撤回 M-N1。（唯一有效残留：`NavMeshBakeConfigLoader.ValidateRaw` 不校验 layer id 大小写约定的跨 mod 一致性——但这是单场景面包，无此要求。）

### 撤回 R-M-D2：east_asia `defaultProfileId=Small` 与 base `light` 冲突 ✘（数组整体替换）

上一稿说 east_asia 的 `agentsPerTeam=0`、`defaultProfileId=Small` 合并时可能与 base `light` 冲突。**验证推翻**：`ConfigMerger.MergeObject` 对数组是**整体替换**（非数组拼接），`agentProfiles.profiles` 被 east_asia 的 `[Small, Medium]` 整体替换，`defaultProfileId` 被覆盖为 `Small`，于是 `Validate()` 中 `defaultProfileId=Small` 能在 `[Small, Medium]` 中找到 → 校验通过。**不是冲突。** 撤回。

---

## 第三部分：补充的配置 SSOT 观察（非 bug，属加固建议）

### O-1：`agentProfiles` 的 `visualScale`/`speedCmPerSecond` 与 `solver.minVisualScale`/`minNavMass` 无 fail-fast 关联

`solver.minVisualScale=0.01`、`solver.minNavMass=0.001` 是运行时兜底阈值（`SetUnitRuntimeProfile` 在 `~:555-564` 抛出 `InvalidOperationException`）。若某个 profile 的 `visualScale` 或 `navMass` 被配置得低于这些阈值，**配置加载时不会拒绝，运行到绑定阶段才抛异常**——fail-fast 缺失。profile 参数与校验阈值之间没有 `Validate()` 层面的关联检查。

### O-2：`flowBlockedCellCost=99999` / `flowBlockedCellThreshold=9999` 是魔法哨兵值

`semantics.solver` 里这两个值必须是"明显大于正常 cost"的哨兵，但 `Validate()` 只保证 `flowBlockedCellCost > flowBlockedCellThreshold`，**没有和 `crowdStampCenterCost=8`/`crowdStampNeighborCost=3`/`flowObstacleNeighborWeight=5` 等真实 cost 做相对约束**。若把 `flowBlockedCellThreshold` 调到接近普通 crowd cost，普通格会被误判为"封死"。

### O-3：`coincidentPairHashPrimeA/B` 是算法常量泄漏进配置

`coincidentPairHashPrimeA=73856093`、`coincidentPairHashPrimeB=19349663`（经典 spatial hash 素数）出现在 `MassNavigationConfig.json` 里，被 `Compute...`（`~:2581-2583`）直接用于 hash 混合。`Validate()` 只要求 > 0。改值会改变碰撞分布——这是**算法参数应内聚在实现、而非暴露在配置**的例子。若要更换 hash 实现，这两项配置成为遗留。

---

## 第四部分：修订记录

| 条目 | 上一稿结论 | 验证结果 | 处理 |
|---|---|---|---|
| M-C1 CrowdPhysicsArena 死配置 | 高 | **推翻**：配置经 base 全局注册加载，运行时激活正常（boot test 通过） | 撤回 |
| M-C2 mapId 三源不一致 | 高 | **部分推翻**：多数一致；唯一不匹配是 ParticipantViews 刻意的禁用哨兵 | 撤回（留 O 级建议） |
| M-D2 east_asia defaultProfileId 冲突 | 中 | **推翻**：数组整体替换，校验通过 | 撤回 |
| M-N1 navmesh 副本漂移 | 中 | **推翻**：各 mod 独立面包配置，非漂移 | 撤回 |
| K-P1 road_network flowCellSizeCm 继承 | — | **确认**：500×500 网格 ≈ 7.6MB | 保留（加固建议） |
| K-P4 Sonar `*0.5f` | — | **确认**：全角/半角隐式换算 | 保留 |

---

### 关于 P0（右键下单）的修复

上一稿未覆盖、但本次实际复现并修复的真正的生产阻断：**10K showcase 右键下单不可用**（`CommandCountFrame == 0`，日志 `ORDERSRC bind failed actor=Entity = { Id = 0... }`）。

根因：`MassNavigationLargeWorldLocalOrderSourceSystem` 用 `GetControlledActor()` 解析输入归因 actor，但该 showcase 无单一玩家拥有的 avatar，所以 `GetControlledActor()` 永远返回 Null；且独立下单的 `CommandGroupToken` 用 `OrderId` 关联，原子扇出（AdmissionBatch）下无法正确关联成同一个 command group（#714-719 唯一 orderId 迁移破坏项）。

修复（对齐已验证的 `fix/massnav-e2e-order-chain` 分支提交 `6a0ecaccce`）：
1. `MassNavigationLargeWorldLocalOrderSourceSystem`：直接绑定 `ClientLocalSeatAccess.TryGetSolePossessedRep` 的 seat rep 作为输入归因 actor（该 showcase 无单一玩家 avatar，`CommandSource-primary` 解析永远无 actor）。
2. `LocalOrderSourceHelper.TryGetCommandSourceOwner`：委托给 accessor `TryGetCommandSourceOwner`，消除重复实现。
3. `OrderQueue.Order.CommandCorrelationId`（新增）：原子扇出经 `AdmissionBatchId` 关联、独立单发按 `OrderId`。
4. `MovePlanOrderLifecycleSystem`/`MovePlanOrderProjectionSystem`：`CommandGroupToken` 统一改用 `CommandCorrelationId`。

验证：MassNavigation 全量 **114/114 通过**（原 113/114，P0 失败）。GasTests `Order` 切片 **401 通过 / 20 失败**——与 clean baseline 完全一致（20 个失败为基线固有，非本次回归）。

> 注：`6a0ecaccce` 提交还包含 `.github/workflows/solution-verify.yml`（补右键 E2E 门禁过滤 + RngCoreTests 折行修复）与 `#1128` 相关改动，以及 `OrderQueue.cs` 的其它上下文。本次只移植了与 P0 直接相关的 5 处代码逻辑；CI workflow 与 RngCoreTests 改动未在当前工作树应用（属独立事项）。

本报告只增补审计结论，未再改动其它生产代码。基线提交仍为 `81a1b5f543`。
