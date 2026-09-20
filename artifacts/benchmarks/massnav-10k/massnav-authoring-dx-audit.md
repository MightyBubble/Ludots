# MassNavigation 作者体验审计：给单位配寻路的真实成本

审计对象：`mods/capabilities/navigation/MassNavigationMod`（基座）、`mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod`（10K showcase）、对照 `CapabilityStandardCrowdPhysicsArenaMod`、`FormationCapabilityShowcaseMod`。
只读审计，未改任何 src/mods 文件。分支 `codex/gpu-skinned-commercial`，2026-09-13。

## 结论先行

让一批单位有寻路，今天的必填面是 **1 份 161 键零默认值的 MassNavigationConfig.json + 约 400 行 mod 侧 C# + 5 类文件间的手工同步**。其中约 15 个键被解析和强制校验但运行时零消费，约 22 个 game.json presentation 键是 Core 基线的原样拷贝，两套共约 290 行的展示/输入系统在两个 mod 之间是近乎逐行的复制。引擎本体（系统安装、solver、绑定）已经是自动的，摩擦集中在配置合同与 mod 侧样板，不在引擎内核。

---

## 1. 最小可用集盘点

### 1.1 MassNavigationConfig.json（基座 322 行 / 161 个叶子键）

解析与校验 SSOT：`src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`。加载器要求 catalog 以 DeepObject 策略注册（`MassNavigationConfigLoader`，同文件 ：574-634），mod 间按同路径 overlay 合并。

逐节分类（键数为叶子键计数，数组算 1）：

| 节 | 键数 | 必填性 | 分类 |
| --- | --- | --- | --- |
| `mapId` | 1 | 必填 | 必须保留（map 匹配 SSOT） |
| `world` | 9 | 全必填（:104-115） | `solverWindowWidth/HeightCm` 与 solver.fieldWidth/Height 必须相等（:957-962，冗余双写）；`hotZones`/`activeHotZoneId` 是 "hotspot debug landmark"（:953-954 注释自认 debug）却必填 ≥1 |
| `solver` | 13 | 全必填（:116-130） | `playAreaMin/MaxXCm`、`playAreaMin/MaxYCm` 4 键可由 field 尺寸 ±margin 推导（在用配置里就是 50..9950 对 10000 场）；其余有物理含义 |
| `presentation` | 6 | `requiredMeshAssetIds`/`blockerTemplateId`/`teams` 无条件必填（:131-136） | `teams[].styleId` 死键（见 §2）；autoSpawn=false 时 `requiredMeshAssetIds` 仍必须非空（:674-677）但无人校验/消费（见 §2） |
| `scenario` | 5 | 必填；autoSpawn=false 时 `spawnLayout` 可为 null | `agentsPerTeam`+`teams` 是规模 SSOT，保留 |
| `scenarioRuntime` | 12 | `autoSpawnConfiguredScenario` 布尔 + 11 个容量键全必填（:85-102） | 11 个容量键中至少 5 个（`groupMembershipAgentCapacity`/`groupMemberCapacity`/`movePlanExecutionMemberCapacity`/`relationshipDomainCapacity`/`loadedChunkCapacity`）已被 `ValidateForScenario`/`ValidateForStreaming` 用规模关系复算（:498-551），可以推导后留 override |
| `cadence` | 9 | 全必填（:160-171） | 有物理含义；`agentSliceCount` 有默认值 1（`MassNavigationCadenceConfig.cs:24`），是唯一带默认的键 |
| `agentProfiles` | 2 | 全必填（:172-197），每个 profile 6 键 | 保留（速度/体型/比例是作者语义） |
| `teamRelationships` | 2 | 全必填（:199-201） | 保留 |
| `relationshipPolicy` | 1 | 必填（:202） | 消费于 `MassNavigationRuntime.cs:160-163`，保留 |
| `flow` | 6 | 全必填（:207-214） | `enabled=false` 时整节惰性；`maxIterationsPerStep`、`forceRefreshFlow/Crowd/Obstacles` 4 键零消费（见 §2） |
| `arrival` | 13 | 全必填（:215-229） | 其中 8 个 min/max 键零消费（见 §2），实际语义键 5 个 |
| `avoidance` | 29 | mode + orca 2 键 + sonar 9 键 + 17 个共享键全必填（:230-266） | mode=Separation 时 orca/sonar 两节共 11 键不进任何分支（`MassNavigationFlowSolverState.cs:2471-2472`）；共享 17 键有消费 |
| `semantics` | 51 | 全必填（:267-329） | `solver` 子节 22 键是引擎内部常数（epsilon、hash 质数 `coincidentPairHashPrimeA/B`），暴露为必填作者键；`facingVelocityEpsilonSq` 死键（见 §2） |
| `streaming` | 2 | 必填（:203-206） | 保留 |

反序列化用 `StrictJsonOptions.CreateCamelCase()`，`UnmappedMemberHandling.Disallow`（`src/Core/Config/StrictJsonOptions.cs:14`）：拼错键名会立刻报错，没有"静默忽略"类问题；问题反面是**所有已知键全部必填、没有一个可选键**（除 `agentSliceCount`）。

### 1.2 templates.json 与 config 的耦合（基座 467 行 / 12 模板）

- 单位模板通过 `MassNavigationAgent` 组件的 `profileId` 字符串引用 `agentProfiles.profiles[].id`（`templates.json:70-72`）——字符串约定，无编译期检查，错名在运行时 `MassNavigationProfileRegistry` 才炸。
- autoSpawn 路径下模板 id 由 `presentation.teams[].lightTemplateId/heavyTemplateId` 按队绑定（`MassNavigationScenarioBootstrap.cs:79-80` 用 `ResolveAgentTemplateId`），presenter id 同理 `lightPresenterId/heavyPresenterId`。同一队的信息要在 config 的 teams 数组里写 4 个 id，与 templates.json、presenters.json（基座 1143 行）三方对齐。
- 想被玩家框选下令，模板还要 `CommandSourceSelectableTag`/`CommandSourceSelectableState`/`OrderBuffer`/`Unit.Commandable` tag（`templates.json:44-61`）。

### 1.3 地图侧（Maps/mass_navigation.json，基座 169 行）

- 需要 `Boards[0]` 声明 `SpatialType: Grid` + `WidthInMacroTiles/HeightInMacroTiles/GridCellSizeCm/ChunkSizeCells/LoadedChunkCapacity`。
- 两处跨文件同值同步：
  - `world.streamingChunkSizeCm: 6400`（config）== board `ChunkSizeCells: 64 × GridCellSizeCm: 100`（map）。运行时硬校验相等（`MassNavigationSimulationRuntime.cs:327-330`），但值要作者在两个文件里手工保持一致。
  - game.json `worldWidthInMacroTiles: 250` == map `Boards.WidthInMacroTiles: 250`。`GameEngine.cs:540-548` 用 game.json 值建 `WorldMap`，map 侧再声明一遍。
- `MassNavigationRuntime.BindBoardWorld`（`MassNavigationRuntime.cs:223-237`）要求 board 的 `LoadedChunks` 必须是 `WorldGridLoadedChunks`——即 map 必须用 Grid board，这是隐性合同。
- autoSpawn 路径还要求 map 拥有 `ContinuousHeightmap` 资产且实现 `IContinuousHeightmapRenderSource`（`MassNavigationAuthoringContract.cs:66-71`）——纯逻辑寻路也被强制绑地形渲染资产。

### 1.4 GAS 与展示是否可选

- **GAS order 不可选**：ModEntry 安装 order adapter 时 `orderTypes.TryGetId(MassNavigationOrderKeys.Move)` 失败直接抛异常（`MassNavigationModEntry.cs:56-62`），即 `GAS/order_types.json` 定义 `massNavigationMove` 是寻路命令链的硬前置。单位不发命令纯 AI 驱动的话另说，但"玩家可指挥的寻路单位"必须配。
- `onSpawnEffect: "Effect.MassNavigation.Agent.HealthDrift"`、血条、HUD、minimap 都是展示层，可删，寻路本身不受影响。
- 但有个隐性必要件：不做知识披露就看不到单位。10K mod 为此手写了 209 行 `MassNavigationObserverVisibilityBindingSystem`（见 §3.2），并设 `PresentationAudienceRevealHidden` service（ModEntry :44）。

### 1.5 C# 侧：谁自动装、谁要手写

引擎已自动装（`GameEngine.cs:254` 持有 runtime，`GameEngine.MapLoadLifecycle.cs:494/617` 在 map focus 时触发，config.mapId 匹配即激活，`MassNavigationRuntime.EnsureSystemsInstalled` :112-148）：

- `MassNavigationAgentMetadataSyncSystem` / `MassNavigationSimulationStepSystem` / `MassNavigationPreSimulationStepSystem` / `MassNavigationAuthoredAgentBindingSystem` / `MassNavigationEnvironmentBindingSystem` / `MassNavigationMovePlanExecutionSystem` / `MassNavigationLocomotionAnimatorParamSystem` + PoseAuthority 桥监听。

mod 仍要手写的：

| 样板 | 行数 | 为什么还在 mod |
| --- | --- | --- |
| `MassNavigationModEntry.InstallMovePlanOrderAdapterAsync`（MovePlanOrderProjectionSystem + MovePlanOrderLifecycleSystem） | 74 | 注释自述 anchor 在 `MassNavigationMovePlanExecutionSystem` 上（引擎装的），但 orderType id 要查 GAS registry（mod 数据）。数据/系统归属割裂，没有引擎侧安装点 |
| 10K `MassNavigationLargeWorldLocalOrderSourceSystem` | 80 | 包装 `LocalOrderSourceHelper`（CoreInputMod 已有）+ 幂等安装 + 失败日志 |
| 10K `MassNavigationObserverVisibilityBindingSystem` | 209 | 向 `KnowledgeProjectionStore` 发布全部 agent 的 LiveVisible 披露，否则 presenter/minimap/选择不工作 |
| 10K `MapFocus` helper + ModEntry 骨架 | 19+88 | 事件挂接、minimap 预设、安装幂等键 |

10K mod C# 合计约 396 行。

---

## 2. 配置键废弃扫描（证据）

判定标准：属性被解析、被 `ValidateRequiredTopLevelProperties` 强制，但在 Core 内除定义/自校验/CopyFrom 外无任何读取点。全量扫描方法：对每个 tuning 类的 public 属性做全 Core 源码词边界 grep（cadence 的 Hz 键经同文件调度器消费，为消费键；不误报）。

| 死键/可疑键 | 必填证据 | 零消费证据 |
| --- | --- | --- |
| `presentation.teams[].styleId` | `MassNavigationConfig.cs:883-886` RequireNonEmpty | Core 全源码 0 引用 |
| `arrival.timeoutMinMs` / `timeoutMaxMs` | `MassNavigationConfig.cs:218-219`；`MassNavigationFlowArrivalTuning.cs:47-48,54,68` | 仅自我范围校验，运行时 0 读取 |
| `arrival.progressDistanceMinCm` / `MaxCm` | 同上 :222-223 | 同上 |
| `arrival.wakePushDistanceMinCm` / `MaxCm` | 同上 :225-226 | 同上 |
| `arrival.maxRetryCountMin` / `Max` | 同上 :227-228, :62-67, :71 | 同上（`maxRetryCount` 本体有消费） |
| `flow.maxIterationsPerStep` | `MassNavigationConfig.cs:212` | 0 读取（`iterationsPerStep` 有消费且仅在 flow.enabled=true 时） |
| `flow.forceRefreshFlow/Crowd/Obstacles` | `MassNavigationConfig.cs:213-214` | 唯一出现点是 `MassNavigationFlowSolverState.cs:1176-1178` 写回 false，从未被读 |
| `semantics.solver.facingVelocityEpsilonSq` | `CrowdSemantics.cs:279,333` | Core 0 读取；唯一"消费者"是 `CapabilityStandardTimeFlowShowcaseConfig.cs:302` 的镜像副本 |
| `avoidance.orca.*`（2 键）+ `avoidance.sonar.*`（9 键）在 mode=Separation 下 | `MassNavigationConfig.cs:253-266` 无条件必填 | `MassNavigationFlowSolverState.cs:2471-2472`：Separation 分支 return，两节不进任何路径。在用两份 config 均为 `mode: "Separation"` |
| `presentation.requiredMeshAssetIds`（autoSpawn=false 路径） | `MassNavigationConfig.cs:674-677` 无条件要求 ≥1 | `MassNavigationAuthoringContract.cs:50-58`：该路径提前返回，mesh registry 为 null；:122-126 `ValidateAll` 只在 autoSpawn 时校验模板/presenter。FormationCapability 被迫填一个永不检查的 `"mass_navigation.agent.soldier"`（其 config :4-6） |

合计：**无条件死键 13 个（arrival min/max 8 + flow 4 + styleId）+ 条件死键 12 个（orca/sonar 11 + facingVelocityEpsilonSq）+ 路径仪式键 1 组**，占 161 键的约 16%。

game.json 侧（10K mod `assets/game.json`，31 个 presentation 叶子键）与 `LudotsCoreMod/assets/game.json` 基线逐键对比：

- 22 键与基线**逐值相同**（presenterCommandCapacity、primitiveDrawBufferCapacity、visualSnapshot/ProxyBufferCapacity、presentationRequestCapacity、groundOverlay/splineRibbonCapacity、worldHud/screenHudCapacity、minimapMarkerCapacity、runtimeEntitySpawn(Queue/Receipt)Capacity、cameraCulling 全部 3 键、minimap 9 键）——纯拷贝。
- 真实差异 7 键（presenterInstance/gasPresentationEvent/presentationEventStream/presentationOwnerChange 131072、skinnedVisualBatch 131072、minimap.initialZoomNormalized 0.12、debugMarkerSampleCapacity 128）+ 新增 1 键（worldHudTerrainOcclusionCacheCapacity）。
- `gasRuntimeCapacity` 5 键（deferredTriggerPerFrame 16384、orderQueue 16384、orderAdmissionResult 32768、orderAdmissionRejection 16384、orderTerminalResult 16384）相对引擎基线 `assets/game.json`（1024/4096/8192/4096/4096）是真实扩容，非无效键。

无"被静默忽略的键"（Disallow 策略兜底），但存在**必须显式写 null 才能退出继承**的负向摩擦：FormationCapability 用 `"blockerPresenterId": null`、`"teams": []` 抹掉基座 overlay 继承值（其 config :7-10）。

---

## 3. 重复造轮子扫描

### 3.1 mod 间复制的样板

- **ObserverVisibilityBindingSystem**：`CapabilityStandardCrowdPhysicsArenaMod/Systems/CrowdPhysicsArenaObserverVisibilityBindingSystem.cs` 与 10K 的同名逻辑 diff 后仅剩命名差异，前者注释直接写明 "(same contract as the 10k mass-navigation showcase)"。两份各约 200 行。
- **LocalOrderSourceSystem**：全仓 13 个 mod 各持一份 `*LocalOrderSourceSystem`（arpg_demo、moba_demo、rts_demo、formation_capability、interaction、road_network、ux_prototype、crowd_physics_arena、massnav-10k、browser_rts_production、champion_skill_sandbox、fireball_shared 等），全部是 `LocalOrderSourceHelper`（CoreInputMod 已提供）+ 幂等初始化 + 日志的包装。
- **语义常数镜像**：`CapabilityStandardTimeFlowShowcaseMod/Runtime/CapabilityStandardTimeFlowShowcaseConfig.cs:283-380` 用私有类 `TimeFlowNavigationSemanticsConfig` 复刻了 semantics.solver/steering/obstacle 约 30 个键并 `ApplyTo` 回写——因为 MassNavigationConfig 没有默认值，想要不同语义的 mod 只能整表镜像再覆盖。

### 3.2 massnav 自带机制 vs Core 已有物

- 关系域投影：massnav 已经正确复用 Core 的 `DomainStanceQuery`（`MassNavigationRuntime.cs:158-163` 构造 `MassNavigationDomainStanceProjection`），未重复造轮子。
- 知识可见性：Core 有 `KnowledgeProjectionStore`、`DynamicParticipantVisibilityPublisher`（`src/Core/ParticipantVisibility/`）、`FogKnowledgeProjector`（`src/Core/Vision/`），但没有"本地观察者全量 LiveVisible"这个最常用档位的通用件——每个 showcase 只能手写一遍发布循环。10K 的 209 行系统本质是一个 60 行的通用披露策略 + massnav 特有的 presenter mask 推导。
- 分组 runtime / 容量：`runtimeCapacity` 的 11 键与 scenario 规模的关系全部可推导（§1.1 表），引擎已经在校验里算了这些关系，只是把结果反过来要求作者先填。

### 3.3 overlay 机制本身是好的，但基座耦合重

CrowdPhysicsArena 的 config 只有 62 行 / 11 个覆盖键（mapId、hotZones、scenario、presentation.teams 复用基座 presenter id、displacedAgentCapacity），证明 overlay 分层可行。代价：

- 必须整段重述 `scenario.spawnLayout`（含与基座相同的 `randomSeed: 12648430`）和 `teamRelationships`（数组语义是整替不是合并）。
- 继承面不透明：作者继承的是另一张地图的 `world.hotZones`（central/east_front/southwest/northeast，坐标是 10K 大世界的）、10km solver 窗口、全部 cadence/semantics 常数——想改一个键必须先知道基座 161 键的现值。

---

## 4. 文档与示例现状

- `gitbook/reference/mass-navigation-user-book.md`（170 行）：面向 RTS 集群移动，参考实现指向 FormationCapability。§"做自己的 RTS Mod"的第一步原文是"**复制 showcase 的 Mod 目录结构和配置入口**"——抄作业是被文档认可的唯一路径，没有最小化路径。
- 局部参考：`agent-profile.md`（80 行）、`obstacle-authoring.md`（144 行）、`map-scale-authoring-guide.md`（246 行）。
- 没有 MassNavigationConfig 的键级参考（161 键中哪些必填、哪些有默认、哪些是引擎常数无从查询，只能读 `MassNavigationConfig.cs` 的校验代码）。
- `gitbook/navmesh-features/`（route/route-execution 等 18 页）讲的是 NavMesh 路由域体系，与 `MassNavigationConfig` 执行层的关系没有作者向衔接页。
- 没有"三步给单位加寻路"式的一页纸。

## 5. 量化对比

现状（以 10K showcase 为例，叠加在基座之上）：

| 项 | 现状 | 说明 |
| --- | --- | --- |
| 必填 config 键 | 161（基座，全必填） | 其中约 25 键死/条件死/可推导 |
| mod 侧 C# | ~396 行（10K）/ ~74 行（基座 ModEntry） | 全部是安装样板与披露样板，无玩法逻辑 |
| game.json 键 | 31 presentation + 5 gas | 22 个 presentation 键是基线原样拷贝 |
| 手工同步点 | ≥3 处 | solverWindow==fieldWidth；streamingChunkSizeCm==board chunk；game.json worldSize==map board size；另有 config teams × templates.json × presenters.json 三方 id 对齐 |
| 新作者文档入口 | 1 份 170 行上手书，路径是"复制 showcase" | 无键级参考 |

理想态（同等能力）：

| 项 | 理想 | 达成手段 |
| --- | --- | --- |
| 必填 config 键 | 15–25 | mapId + 规模（teams/agentsPerTeam）+ profile（速度/半径）+ 少量 override；容量/playArea/世界窗推导，引擎常数收进代码默认，死键删除 |
| mod 侧 C# | 0 行 | order adapter 随引擎装；LocalOrderSource 与观察者披露成为 CoreInputMod/Core 的 config 驱动标准件 |
| game.json | 7–9 键 | 只留真实差异（10K 实测真实差异 8 键） |
| 手工同步点 | 0 | board 为 SSOT；worldSize 单一来源；模板↔presenter 由同一处声明或按约定解析 |
| 文档 | 1 页三步走 + 键级表 |  |

## 6. 修复建议分级

**P0 删死键/条件化必填（纯减法，无行为变化）**

1. 删 `arrival` 8 个 min/max 键、`flow.maxIterationsPerStep`、`flow.forceRefresh*` 3 键、`presentation.teams[].styleId`、`semantics.solver.facingVelocityEpsilonSq`（消费为零）。
2. `avoidance.orca`/`avoidance.sonar` 改为按 `mode` 条件必填。
3. `presentation.requiredMeshAssetIds` 在 `autoSpawnConfiguredScenario=false` 时免填。
4. `world.hotZones`/`activeHotZoneId` 降级为可选 debug 节（autoSpawn 的 hotspot marker spawn 相应条件化）。

**P1 引擎自动装（消灭 mod 侧样板）**

5. MovePlan order adapter（Projection + Lifecycle 两系统）移入引擎侧随 MassNavigationRuntime 安装，orderType 解析失败改为可诊断的 typed error。
6. `LocalOrderSource` 标准件化：CoreInputMod 提供 config 开关（`input.localOrderSource.enabled`）驱动的安装，13 个 mod 删自有副本。
7. 观察者披露标准件化：在 Core（ParticipantVisibility 或 Knowledge）提供"本地观察者对满足查询的实体发布 LiveVisible + presenter mask 推导"的通用系统，massnav showcase 的 209 行收敛为查询描述 + 安装声明。

**P2 推导与去重（消灭跨文件同步）**

8. `runtimeCapacity` 中可由 scenario 规模/streaming 窗口推导的 5+ 键改为"可选 override，缺省推导"，`ValidateForScenario` 的推导逻辑复用为默认值生成。
9. `solver.playArea*` 4 键改为可选（默认 field 尺寸内缩 margin）；`world.solverWindowWidth/HeightCm` 与 solver.field 合并为单一来源。
10. `world.streamingChunkSizeCm` 以 board 为 SSOT（运行时已有相等校验，删 config 键）。
11. game.json `worldWidth/HeightInMacroTiles` 与 map Boards 尺寸合一。
12. 10K mod game.json 删除 22 个基线拷贝键，收敛到 8 个真实差异键（顺带为后来者树立"只写差异"的样例）。

**P3 文档**

13. 新增 `gitbook/reference/` 下"给单位加寻路"一页纸：依赖声明 → 最小 config → 模板组件三步走，附 FormationCapability/外部 authoring 路径。
14. 补 MassNavigationConfig 键级参考表（必填/默认/引擎常数/调试用途四档），与本文 §2 的死键清单互链。

## 7. 建议 issue 列表（待人工审核后开）

1. **[MassNavigation] 清理 13 个零消费必填键（arrival min/max×8、flow×4、styleId）**——删除或降级为可选，校验同步收紧。
2. **[MassNavigation] avoidance orca/sonar 按 mode 条件必填**——Separation 模式下免写 11 键。
3. **[MassNavigation] requiredMeshAssetIds 对外部 authoring 路径免填**——autoSpawn=false 不再要求 ≥1 个永不校验的 mesh id。
4. **[MassNavigation] hotZones 降级为可选 debug 节**——调试地标不应是生产 config 必填项。
5. **[MassNavigation] runtimeCapacity 5 个规模键改为推导默认 + 可选 override**——复用 ValidateForScenario 的推导。
6. **[MassNavigation] MovePlan order adapter 移入引擎侧安装**——消灭每个下游 mod 的 74 行 ModEntry 样板。
7. **[CoreInputMod] LocalOrderSource 标准件化（config 驱动安装）**——一次性删除 13 个 mod 的重复包装系统。
8. **[Core] 本地观察者 LiveVisible 披露通用系统**——吸收 10K/CrowdPhysicsArena 两份近同构系统。
9. **[MassNavigation] 消除三处跨文件同值同步（solverWindow/streamingChunkSize/worldSize）**——board 与 map 为 SSOT。
10. **[showcase] 10K mod game.json 收敛到 8 个真实差异键**——删 22 个基线拷贝，树立"只写差异"样例。
11. **[gitbook] "给单位加寻路"一页纸 + MassNavigationConfig 键级参考表**——补最小化路径与 161 键分级说明。

## 附：证据文件索引

- 基座 config：`mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json`
- 解析/校验：`src/Core/MassNavigation/Runtime/MassNavigationConfig.cs`（RequireProperty 全键清单 :68-330）
- 引擎自动安装：`src/Core/MassNavigation/Runtime/MassNavigationRuntime.cs:112-148`；触发点 `src/Core/Engine/GameEngine.MapLoadLifecycle.cs:494,617`
- order adapter 样板：`mods/capabilities/navigation/MassNavigationMod/MassNavigationModEntry.cs:30-72`
- 10K mod：`mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod/`（ModEntry 88 行、Systems 2 份 289 行、game.json 72 行、config overlay 4 行）
- Arena 复制证据：`mods/showcases/capability_standard/CapabilityStandardCrowdPhysicsArenaMod/Systems/CrowdPhysicsArenaObserverVisibilityBindingSystem.cs`（含 "same contract as the 10k mass-navigation showcase" 注释）
- 外部 authoring 路径：`mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/MassNavigationConfig.json`（72 行，含 null 抹除）
- 语义镜像：`mods/showcases/capability_standard/CapabilityStandardTimeFlowShowcaseMod/Runtime/CapabilityStandardTimeFlowShowcaseConfig.cs:283-380`
- 引擎 game.json 基线：`assets/game.json`（gasRuntimeCapacity 19 键）；presentation 基线：`mods/LudotsCoreMod/assets/game.json`
- 文档：`gitbook/reference/mass-navigation-user-book.md`
