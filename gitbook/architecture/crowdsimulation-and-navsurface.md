# CrowdSimulation 与 NavSurface：导航体系重构

> 状态：S0（配置门禁）、S1（地形与障碍物）、S2（逐体型可走区域与跳跃链接）、S3-a（tile 烘焙链与 tile 缓存）、S3-b（全局拼装 + HPA* 抽象图,拓扑逐位对拍）、S3-c（走廊查询 + 流场 + 固定生效帧路径服务）、S4-a（ECS 单位部署 + 规范校验码 + 指令记录回放内核）、S4-b（部署演示纯数据化：单位经 presenter 管线进画面,自动部署 + 自动回放对拍）、S5-a（移动内核与阵型：虚拟领队 / 意图 / 马达 / 槽位 / 规划器,轨迹对拍）、S5-b（最小避让：非对称分离 / 关系推挤矩阵 / calm 休眠,L02 校验码运动字段重钉）、S7-a（结构动态化：建造/拆除/寿命 + 脏 tile 增量重烘 + HPA 增量 + 队伍反应）、S7-b（迷雾/视野组/认知变体:视野组并查集、周期 slot 迷雾、认知槽变体导航、原位揭示、D50 面命令,digest 逐位对拍）已交付。拖线摆阵演示与走廊扩展、S6 剩余（态度模式推挤 / 阻挡盒推挤）见文末路线。

## 这是什么

CrowdSimulation 是新的确定性群体导航内核：分层地表（NavSurface）、逐（移动类型 × 净空）的导航上下文、HPA* 走廊 + 流场、虚拟领队与阵型槽位、局部避让与推挤、脏 tile 增量重烘焙、战争迷雾与认知导航。行为参照是 CrowdSimulation Web 沙盒（Base44 参考实现，16 km 地图、5 万单位上限），交付节奏按该项目的 S0–S8 阶段与 UAT（F-01–F-13）执行。

NavSurface（`.navsurface`）是新增的地表资产：逐导航格的地形类型编号 + 跳跃候选。它是导航的唯一地表输入；高度真相仍在 `.height`，桥面不写进地表——桥实体在运行时生成独立的桥面层，拆桥即恢复。

## 为什么不用 MassNavigation 求解器

四个结构性原因（证据见 Web 规格 LU-12 ~ LU-15）：每个成员同步寻路且每次调用都重建网格；地图变化不会让路线失效；方向场是启发式而非沿路网积分；避让与到达规则参数无法一一对应。调参数对齐不了行为，所以内核按 CrowdSimulation 重写。

并存期的纪律：**一张地图只能启用一种求解器**（加载时按 mapId 互斥检查），同一单位模板不能同时带 `CrowdSimulationAgent` 与 `MassNavigationAgent`。MassNavigation 的删除不做在对拍通过之前——它仍被 GAS 下令链、Physics2D 桥、10k 性能门禁与 showcase 引用，对拍基线建立后按 S8 之后的决议退役。

## 数学一律走 Ludots 定点体系

不引入第二套数学：内核全部使用 `src/Core/Math/FixedPoint` 的 Fix64 / Fix64Math（Q31.32 与其确定性超越函数），长度单位为厘米，与 `WorldPositionCm` 同单位制。配置文件维持 Ludots 约定（厘米、`Cm` 后缀），世界距离在组装层按 `厘米 / profiles.cellSizeCm` 转成导航格 Fix64，内核不再做单位换算。四种格的名字、单位和配置键以 [空间尺度 SSOT](spatial-scale-and-resolution-ssot.md) 为准：导航格 `NavCell`、导航 tile `NavTile`、迷雾格 `FogCell`、避让哈希格 `AvoidanceHashCell`。HPA 的一块就叫 tile。Web 侧的 `DetMath` 计划由此废止——对拍时以 Ludots 定点语义为准生成基线，Web 参考实现如需对拍要适配同一套定点算法。

**甲方定案（2026-10-09）：要的是行为对拍，不是数值逐位。** 不为对齐参考端的 f32 存储量化格而在 C# 侧做二次量化（L18 证据链证明逐位一致在双数值系下不可达，量化对齐 = 拿甲方数系迁就参考端的存储层）。验收口径定格为：结构性字段（生成位置、状态机 state/level/order、重烘焙报告、指令序）逐位；派生量（轨迹位置、mode、接触计数骑线）按实测带宽与容忍线，口径一旦写明不接受"再收紧"诉求——系统性分歧（接触集大面积错、状态机翻字、重烘报告字段差）才是这道门要拦的东西。

烘焙期边界：运行期内核（部署 / 移动 / 规划）全 Fix64；`NavHeightField` 的 .height 样本解码与 `NavTileBaker` 的简化 / 边长阈值比较、格心归属判定允许 IEEE double，产物进定点域（定点厘米 / 整数格角点）前一次性转换，依据见 S2 段——定点域内直接乘会溢出 Q31.32，且 Web 导出端同样从量化样本重建，两端读同一份输入。

## 目录落点

```
src/Core/CrowdSimulation/            内核（命名空间 Ludots.Core.CrowdSimulation.*）
  Config/                            CrowdSimulationConfig（DTO，严格 camelCase 全必填）
                                     CrowdSimulationConfigValidator（数值 + 结构规则）
                                     CrowdSimulationConfigLoader（LU-20 组装入口）
                                     CrowdSimulationAuthoringContract（模板级契约,进图激活时执行）
  World/                             NavSurfaceAsset（.navsurface v1 读写与格式门禁）
                                     NavSurfaceContract（资产 ↔ 配置一致性）
                                     CrowdSimulationSpace（格几何原语）
                                     SurfaceGrid（地表栅格:地形 / 区域 / 阻挡）
                                     NavHeightField（.height → 导航高度场与坡度）
                                     CrowdSimulationMapSurfaceSource（地图实体提取）
  Nav/                               NavContextBaker（分类 / 腐蚀 / 连通域 / 链接）
                                     UpperLayerBake（桥面层栅格化与烘焙、portal 合并）
                                     NavContext / NavLinkSet（上下文产物）
  CrowdSimulationAgent.cs            单位组件：profileId、agentTypeId、speedCmPerSecond、personalRadiusCm、pushPriority、special
mods/capabilities/navigation/CrowdSimulationMod/
  assets/config_catalog.json         登记正式配置。调试文件由提供它的 Mod 自己登记
  assets/Navigation/agent_profiles.json
                                     体型档案。layer 是 NavTile 层，navProfileId 指向能力档
  assets/Entities/templates.json     CrowdSimulationAgent 写速度、个人半径、推挤优先级
mods/capabilities/navigation/CrowdSimulationStandardMod/
  assets/CrowdSimulationConfig.json  标准参数（对拍同一份）。不含地图事实，不写时钟
  assets/Navigation/navmesh.json     能力档、区域、坡度。群体地图的地形事实在 maps.<mapId>
  assets/Navigation/pathing.json     移动类型和区域代价。selection / nodeGraph 可省略
src/Tests/CrowdSimulationTests/      S0 验收测试
```

目录与 Web 参考实现 `src/engine/` 的模块对照按 TR-04 执行；后续阶段（Nav / Planning / Movement / Crowd / Sim / Fog / Threads）陆续补齐。

## 复用的既有设施

- ConfigPipeline + `config_catalog.json`（DeepObject 合并；缺键 / 未知键拒绝）
- `AgentProfileRegistry`（`Navigation/agent_profiles.json`，ArrayById）
- `MapConfig`（Boards / Teams / Players / ParticipantRelationships / Entities）
- `Engine/clock.json` 的 `FixedHz`（仿真频率的唯一来源，CrowdSimulationConfig 不再有自己的 tickRate）
- 组件注册走 `ComponentRegistry`（`CrowdSimulationAgent` 已登记）
- 测试沿用 NUnit + `src/Tests/*` 项目格局

## 与主干平行组件的存留理由

行为对拍（2026-10-09）之后，下面五处仍不收进主干对应物。每处写自己还在的原因，不用「替换即破对拍」一句盖过去。结构性字段（部署位置、抽象图弹出序、指令日志）仍然逐位。领队折线和接触子集走的是已经写明的行为带宽。这次五处都没有换成主干类型。

- `Nav/NavMinHeap` 对 `Collections/Fix64PriorityQueue<T>`。参考对应物 `engine/heap.js`。比较规则已经对齐：同键不上升，下滤时同键留左孩子。仍单独留着，是因为 Dijkstra 用的是 Fix64 键和 int 下标两列，弹出后不把死槽写成 default。`Fix64PriorityQueue<int>` 把键和值包进一个结点，`Clear` 会把死槽清零。S3-b 抽象图拓扑的 FNV 挂在这个弹出序上，弹出序是结构性字段。替换前要用现有拓扑对拍证明弹出序没变。这次不换。
- `Units/CrowdSimRng` 对 `Engine/Randomization/RngStreamService`。参考对应物 `engine/noise.js` 的 mulberry32。两条流算法不同（RngStream 是 xorshift32），输出也不同（RngStream 的 NextFloat01 是 24 位整数 ÷ (2²⁴−1)；CrowdSimRng 是 u32 ÷ 2³²，直接进 Fix64 原始值）。部署按指令加盐：`CrowdDeployment` 用 worldSeed × spawnSeq 给每条指令自己的流。RngStreamService 是先声明后取用的命名流。生成位置是结构性字段，S4 要求 12020 个单位的位置原始值逐位一致。换流会改这些位置。
- `Units/CrowdSimCommands` + `CrowdCommandQueue` 对 `Persistence/ReplayRecorder`。参考对应物 `engine/core/commands.js` 的 CommandQueue。指令队列是内核输入面：tick 边界执行、submit 现场记日志、schedule 与存量按 tick 归并、迟到指令按实际执行 tick 记录。载荷在入队边界一次解析为 struct（`CrowdSimCommand.Parse`），JsonNode 不进队列、日志和回放。日志是完整输入，回放是新会话加上同一份日志。它记的是仿真输入指令，不是引擎权威帧。帧合同装不下这份日志，见下一节。
- `Nav/NavFunnel` 对 `Navigation/NavMesh/Bake/FunnelAlgorithm`。参考对应物 `engine/navquery.js` 的漏斗。判等不同：NavFunnel 用 Fix64 精确等（`ax == rx`），主干是 float32 Vector2 加 1e-6 容差。漏斗折线直接喂给领队。退化 portal 上差一个顶点，后续轨迹会走出 S5 已写明的位置带宽。这次不换。
- `Movement/CrowdSpatialHash` 对 `Spatial/SpatialHashGrid`。参考对应物 `engine/core/spatialHash.js`。它不只是就近查询。环预计算、逐单位 itemRing、邻接槽位表、唤醒岛、stride 行错峰一起决定接触的枚举顺序。maxNeighbors / maxScan 截断时，顺序决定看见哪些邻居。接触计数的行为门（S5-b 双容忍线）挂在这个子集上。格几何差一点，截断后的邻居集就不同。主干空间结构不保证遍历序，也没有这几张表。这次不换。

### 指令日志能否由 ReplayRecorder 承载：不能

边缘接入评估的结论：记录面留在 `CrowdCommandQueue`，内核语义不变。指令载荷 struct 化（类型/玩家/坐标/形状/选项在边界一次成形）后逐条复核过：第 2、3 条原样成立，第 1 条换了论据、结论不变。阻塞理由：

1. 帧合同装不下，与指令侧表示无关——struct 化前装不下 JSON 指令，struct 化后也装不下 struct 指令。`AuthoritativeAction` =（ActionId 字符串，Vector3 float32，三个布尔），且帧内 ActionId 按字典序严格唯一、浮点必须有限：同 tick 多条指令的提交顺序（归并语义是"同 tick 先到先执行"）没有落点；变长载荷（poly 顶点）与任意字符串（type / shape / template）同样没有无损落点。把序列化指令塞进 ActionId 是新的有损适配层，压在指令日志这份结构性输入上。
2. 检查点生产者不存在（struct 化不涉及）。`ReplayRecorder.SetCheckpoint` 要求 `WorldSaveSnapshot`（engine.World 二进制 + ModSetHash / RegistryFingerprint 头），只能由 `WorldSnapshotService.Capture(GameEngine, boundary)` 产出；对拍与回放会话是一次性 `ArchWorld.Create()`（无 GameEngine，无 save participant 注册表）。
3. 播放端同样要 `GameEngine` + `WorldRestoreService`（`ReplayPlayer.PlayFromCheckpoint`）；crowd 回放契约"新会话 + 同一日志"已由 `CrowdSimulationRuntime.RunReplay` 与 `S4DeployTruthTests`（同一记录回放两次校验码相同）覆盖，换播放端不增加能力，只增加依赖。

重开条件：引擎权威帧合同能无损表达指令流（同 tick 顺序、变长载荷、任意字符串），且 crowd 会话具备检查点的生产与消费管线。

## 运行时合同：并存权属、存档、停摆

三条边界先写死，后续阶段在此之上做增量，不重开。

### 并存权属

- **一图一求解器互斥已在活路径**：配置门禁与实体模板合同都拒绝同一地图同时声明 MassNavigation 与 CrowdSimulation 求解器；这不是待办，是装载即拒的现行规则。
- **crowd 会话对自己单位的位置写权**：单位位置、运动学、仿真身份组件由会话步进独占写入，引擎侧系统（分区同步、呈现投影）只读。会话步进系统注册在 PostMovement 组、排在 `SpatialPartitionUpdateSystem` 之前——分区按 `PreviousWorldPositionCm != WorldPositionCm` 判移动，步进晚于同步会让成员格冻结。其他组件域（GAS 效果、交互上下文）不写 crowd 会话状态。
- **GAS 下令链接入归 S8/TR-07**：现行指令从会话命令队列进入（submit 现场指令 / schedule 脚本）。S3/S4 的键鼠在固定步读取 `AuthoritativeInput` 的 `PressedThisTick`，`Submit` 在会话步进之前，呈现只画。把它改接到 GAS 施法链（CommandIntent → OrderFanout → 会话指令）是 S8 的活，不在本层加过渡桥。

### 存档

不支持中途快照。存档 = 指令日志（`CrowdCommandQueue` 的 sources + log），恢复 = 新会话从 tick 0 回放同一日志——回放逐位一致由每 tick 规范校验码背书（`RunReplay`、S4 真值测试）。引擎世界快照（`WorldSaveSnapshot`）承载不了指令流（同 tick 顺序、变长载荷、任意字符串没有无损落点），也不被 crowd 消费。中途快照（长对局断点续跑）S8 之后按真实需求评估，评估前一律按"回放即恢复"。

### 停摆

固定生效帧是唯一口径：路径答复没按生效帧回来，本 tick 会话停摆（不推进、不降级、不用部分结果），下一 tick 重试；规划器风暴期（12000 单位重规划）表现为 tick 变慢而不是行为分叉。这是跨线程确定性的诚实代价——答复到达时刻依赖线程时序，把"晚到的答复"解释成任何确定行为都会破坏回放逐位。不引入 dt=0 空步或切片推进；50k 单位的帧预算问题归 S8 性能门禁处理，不在语义层打补丁。

## 配置门禁（S0 的核心交付）

加载在进入地图时进行，错误消息写明文件、字段路径与规则，例如：

```
CrowdSimulationConfig.json: navtile.sizeCm（12500）/ profiles.cellSizeCm（6250）= 2，需为 ≥ 4、≤ 128 的整数
```

覆盖的规则：声明式数值范围（与参考实现 schema.js 的 RULES 同源，路径已按 Ludots 文件归属换算）、id 唯一性与容量上限（区域 ≤ 256、移动类型 / 单位模板 ≤ 255、组合 ≤ 255）、引用闭包（pathing 的 areaId、档案、模板的 agentTypeId、deploy.bases.playerId）、地图正方形与格数整除、1–16 个玩家、双求解器互斥、阻挡物只能是正方形 Box（不禁止圆形非阻挡区域实体）。配置文件不再带 `version`。作者再写这个键，加载直接报错。formation.headingInheritDot / formation.mirrorFlipDot 是 Ludots 侧的配置化补充。参考实现里是 planner.js 的内联字面量（0.94 / -0.5），默认值与字面量同值，规则是点积阈值域 [-1, 1] 的两半（mirrorFlipDot ≤ 0 ≤ headingInheritDot）。

## 作者怎么配

参数、地图事实、时钟分三处写。

`CrowdSimulationStandardMod` 是纯数据，路径 `mods/capabilities/navigation/CrowdSimulationStandardMod`。它依赖能力 Mod `CrowdSimulationMod` `^1.0.0`。它装对拍用的全量标准参数：阵型、运动、避让、迷雾、`pairRules`、`sim.simSecondsPerTickSecond`。导航格边长、能力档、区域和移动类型写在同目录的 `Navigation/navmesh.json` 与 `Navigation/pathing.json`。没有工程文件，没有入口，不写 `Engine/clock.json`，也不写某一张图的地表和出生点。

`CrowdSimulationMod` 登记 `config_catalog.json`、单位模板和 presenter，并提供 `assets/Navigation/agent_profiles.json`。单位模板上的 `CrowdSimulationAgent` 必写 `profileId`、`agentTypeId`、`speedCmPerSecond`、`personalRadiusCm`、`pushPriority`、`special`。这些字符串留在模板和配置里。生成时把档案下标、移动类型下标，以及模板上已经写成定点数的速度、个人半径、推挤优先级，写进实体上的 `CrowdSimulationAgent`。组件没有字符串字段，tick 里不再解析 id。速度按模板各写各的。领队步速取本组已生成成员的模板速度最小值；子步数取本图绑定模板里的最大速度。移动类型和地形区域的组合写在 `pathing.json` 的 `areaCosts`：`cost` 管寻路，`speedDivisor` 不写时等于 `cost`，两者都必须大于 0。本图绑定的移动类型必须列出 `maps.<mapId>.navAreas` 里的每一个区域；排除写 `passable: false`，不能省略行，缺行也不当成代价 1 或不可通行。`areas` 为 null 直接报错。绑定集是体型 `navProfileId` 落在这张图 `navProfiles` 里的 pathing 类型，加上本图群体单位模板点名的 `agentTypeId`。没进绑定集的类型不校验，也不建网格查询。核心 `Humanoid` 保持 `PreferMesh`，Mod 的 pathing 不要再写它。`passable` 为 false 时群体代价记 0，Detour 过滤掉这一区域。`special` 为 true 的模板不参与批量部署。群体运行时只绑定被单位模板引用的 agent type，pathing 里没有模板的类型不进运行时表。只加载能力 Mod、不加载标准参数 Mod 时，严格反序列化因缺字段失败。

地图 Mod 写 `world.seed`、`deploy.bases`，以及这张图才有的结构模板，例如 `structures.templates.s7barrier`。群体的 AgentTypeIndex 是被单位模板引用的 pathing agent type，按合并后的属性顺序编号。`maps.<mapId>.terrainAreas` 是有序数组，每项 `{ terrainId, areaId }`，数组下标就是 `.navsurface` 的地形编号。`.navsurface` 从地图的 `ContinuousHeightmap.asset` 推导：去掉 `assets/` 前缀，把 `.height` 换成 `.navsurface`。带导航标签的地图用 `maps.<mapId>.navProfiles` 列出要烘焙和装载瓦片库的能力档 id，数组顺序就是烘焙顺序。绑定了移动类型的地图写 `maps.<mapId>.navAreas`（区域 id 数组）。`terrainAreas` 的 `areaId` 必须是它的子集；结构模板会动态铺上的 `deck`、`road`、`flooded`、`burning` 也写进去。没有 nav 标签，或没有 `navProfiles`，启动时不建网格查询。查询只给 PreferMesh / AutoCheapest、且体型 `navProfileId` 在这张图 `navProfiles` 里的类型。向没建查询的类型要路径会抛错，报文带移动类型 id、能力档 id 和地图 id。`MassNavigationAgent.agentTypeId` 不在绑定集里时，地图把模板绑进会话就失败，报文带模板 id 和 agent id。用到群体模拟的地图 Mod 在 `mod.json` 里同时依赖 `CrowdSimulationMod` 和 `CrowdSimulationStandardMod`（`^1.0.0`）。

覆盖走 DeepObject：后加载的 Mod 按键合并。`formations`、`structures.templates`、`navmesh.json` 的 `profiles` 和 `areas`、`pathing.json` 的 `agentTypes` 都以 id 为键，改一项只写那一项。值为 null 会拒绝。写成数组也会拒绝，报错写明要改成以 id 为键的对象。profiles 和 pathing agentTypes 的目录下标是合并后的属性顺序，不按键名重排。区域运行时按下标取值用的是作者写的 areaId。`maps.<mapId>.navProfiles` 是能力档 id 数组，按下标烘焙和装载，列出的档没有瓦片就是装载错误。`maps.<mapId>.navAreas` 写在 navmesh.json。带 `terrainAreas` 的地图按群体地图装载。再把 `agentTypes`、`navAreas`、`navProfiles`、`unitTypes`、`agents.radiusClasses`、`agents.profilesUri`、`world.navCellSizeCm`、`world.surfaceAsset`、`world.terrainTypes`、`mapId` 写进群体配置，加载直接报错，并指出改去哪份主线文件。

时钟由各地图 Mod 的 `assets/Engine/clock.json` 写 `FixedHz`。引擎要求这个值 ≥ 1。群体配置不再另设 10 到 240 的范围。全局变速走 TimeFlow，不走群体配置。

时间有三种写法：

- 步进频率是 `FixedHz`。`fog.rateHz` 相对它，`avoidance.rateHz` 相对 `FixedHz × 子步`。两者都必须 > 0，并且不能超过对应的步进频率。周期是 `max(1, round(步进频率 / rateHz))`。实际频率打在引擎日志里。超过步进频率直接报错。
- `sim.simSecondsPerTickSecond` 是每个 tick 秒推进多少仿真秒。旧键 `sim.timeScale` 已删除。
- 玩法时长带 `Sec`：`formation.settleTimeSec`、`movement.slotTimeConstantSec`、结构模板的 `lifetimeSec`。调度延迟和每 tick 预算仍用 `Ticks` / `PerTick`，例如 `planning.latencyTicks`、`fog.revealTicks`、`flowfield.maxRebuildsPerTick`。寿命到期 tick 是 `ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)`。
- 速率同样带单位后缀。`movement.blendRatePerSec`、`movement.stallDecayPerSec`、`formation.leaderTurnRatePerSec`、`avoidance.accelerationPerSec` 是 1/仿真秒。`movement.unitTurnRateRadPerSec` 是弧度/仿真秒。`movement.slotCheckIntervalSubSteps` 是子步间隔。旧键出现即失败。

### 关系功能位

群体模拟只认两个功能位，写在 `pairRules`。每个功能位点名一个关系类型 id。这个 id 由标准参数 Mod 填，群体模拟不解释它。

| 功能位 | 方向 | 有这条边 | 没有这条边 |
|---|---|---|---|
| `yieldsTo` | a → b，a 的单位碰到 b 的单位 | 按推挤优先级让路。有效优先级是兵种推挤优先级，移动中再加 `push.movingBonus`。相等对半；低的一方让出 `1 - push.dominantShare`；高的一方只让 `push.dominantShare` | 静止的一方不被推开。自己在动、对方静止时全让。双方同状态对半 |
| `sharesVisionWith` | a → b | 这一方向共享视野 | 这一方向不共享视野 |

`sharesVisionWith` 要两个方向都有才并进同一视野组，并且传递：a 与 b 共享、b 与 c 共享，a 与 c 也在同一组。只写一个方向不合组。

标准参数 Mod 两个功能位都点名 `Friendly`：

```json
"pairRules": {
  "yieldsTo": { "relation": "Friendly" },
  "sharesVisionWith": { "relation": "Friendly" }
}
```

`push.movingBonus` 和 `push.dominantShare` 留在标准参数 Mod，只在让路那一支生效。旧的 `relations`（kinds / self / sameTeam / default / overrides）和 `push.modes` 已删除，再写会在加载时报错，并指出改用 `pairRules`。

地图上的边写在队伍代表实体的 `Relations` 里。边有方向。同队要让路、要共享视野，就在队伍代表上写一条 `To` 指向自己的 `Friendly`：

```json
{ "InstanceId": "team-red", "Template": "logical.team",
  "Relations": [
    { "To": "team-red", "Type": "Friendly" },
    { "To": "team-blue", "Type": "Hostile" }
  ] }
```

没有边就是不让路、不共享视野。群体模拟不把同一个玩家单独算成让路或共享。地图 `Players` 全表的 `TeamId` 必须大于 0，和 `ParticipantBindingResolver` 同一条口径，否则进图、会话激活时报错。`deploy.bases` 指向地图上没有的玩家，同样报错。关系类型 id 没有在 `RelationshipTypeRegistry` 里登记，同样报错。

关系只在会话开始投影一次。投影用 `TeamRelationQuery` 查玩家所属队伍的代表，结果是一张玩家对矩阵。避让和视野组之后只查这张表。每个 FixedHz tick，`CrowdSimulationSessionTickSystem` 调用的 `CrowdSimulationRuntime.Tick` 在指令和运动之前做两件事，用的是会话开始时分好的暂存，成功时不分配。先读每个玩家代表上的 `Team` 组件，和开局队伍号不同就报错。再看关系修订号：没变就不重算矩阵；变了就按这批队伍号重算，矩阵和开局不同就报错。已投影的表保持不动。队伍号相同且矩阵相同，只把修订号记成新的基准。

`Team` 是这一步读的组件。`MemberOf`（玩家代表 → 队伍代表）是主干的队伍真相。改这条边会抬修订号，`RelationshipRuntime` 随即 `SyncTeam`，把队伍号写进 `Team`。只改 `Team`、不改边，修订号不动，所以每个 tick 都读组件。

调试文件 `CrowdSimulationDebug.json` 只由自己带这份文件的 Mod 登记，例如 S3、S4。能力 Mod 不登记。目录里没有这项时，正式配置照常装，会话不自动激活。登记了但合并为空，加载抛错。文件本身是严格 DTO，死键在装载时拒绝。要开路径服务，调试文件里写 `worker.planTimeoutMs`，路径服务读这个毫秒数。

需要实体模板数据的检查在 `CrowdSimulationAuthoringContract`，由 Runtime 在进图激活时执行（配置装载后、烘焙与生成前），共三族：模板双求解器 Agent 互斥、模板 `CrowdSimulationAgent.profileId` 存在于 agent_profiles.json、地图阻挡物只能是正方形 Box。单位模板的（兵种 × 半径级）实例化闭包（模板存在、profile 的移动类型与半径级一致）在同一路径的接线预解析里校验。

## .navsurface v1 格式要点

小端二进制：`magic "LNSF"` + version + 尺寸 / 格边长 + 地形类型表（id 字符串，格值 = 表下标）+ 逐格 u8 地形编号 + 跳跃候选（端点格、落差厘米、格距）。读取侧做精确长度校验（多一字节少一字节都拒绝）、编号越界拒绝、端点越界拒绝。资产必须与配置的 `world.terrainTypes` 逐字同序——编号语义是“表中下标”，顺序错一位全图错位。

## 阶段路线（来自 Web 规格第三部分）

S0 配置门禁 → S1 地形与障碍物（读 .navsurface、阻挡实体生成障碍）→ S2 逐移动类型可走区域与烘焙 → S3 后台路线与流场 → S4 部署与命令回放 → S5 移动与阵型 → S6 避让推挤 → S7 动态地图与迷雾认知 → S8 正式下令链与 5 万单位。上一阶段对拍通过后才进入下一阶段。

## S1 交付：地形与障碍物（本次）

- **导出工具**：`tools/crowdsimulation-export/exportLudots.mjs`（正本在本仓库，运行于 Web 沙盒 `scripts/` 下），把作者态测试场景（AU-07 / AU-08）按种子写成 Ludots 资产——`.height`（CHTM v2，uint16 缩放）、`.navsurface`（v1）、`Maps/<id>.json`（队伍代表 `Relations` / 300 阻挡物 / 桥实体）、配置覆盖片段与 S1 对拍摘要。覆盖片段不写 `relations`，功能位在标准参数 Mod。坐标约定：Web 的米 × 100 = Ludots 地图局部厘米，轴向沿用同一平面朝向。
- **演示 Mod**：`mods/showcases/crowd_simulation/CrowdSimulationS1Terrain{1337,2024,7}Mod`，三个种子各一个 Mod，launcher 选择即切换（config 按 mapId 绑定地图，多地图共用一份配置的问题见缺口）。
- **验收（自动化，`src/Tests/CrowdSimulationTests/Parity/`）**：三种子的地形类型栅格、区域栅格、阻挡栅格、阻挡物 / 桥 / 跳跃候选清单与 Web 导出做 FNV-1a 逐格对拍，全相等；删一个阻挡物栅格变、改模板尺寸全体变、覆盖率阈值（< 0.5 不阻挡）生效；`.height` 经 Ludots 正式读取器加载。
- **验收（目视）**：`artifacts/acceptance/crowdsimulation-s1/index.html` 查看器，`CrowdSimulationMapProbe` 工具从真实资产渲染地形类型着色 / 阻挡格 / 桥与跳跃候选三层视图。

## S2 交付：每种单位能去哪里（本次）

- **导航烘焙**（`src/Core/CrowdSimulation/Nav/`，参考实现 `nav.js` / `navLinks.js` / `navtile/upperLayer.js` / `erode.js` 的 S2 子集移植）：逐（移动类型 × 净空）导航上下文 = 代价栅格（区域代价行 × 坡度上限 × 阻挡）→ 切比雪夫净空腐蚀 → 可走栅格 → 4 连通洪泛连通域 → 跳跃链接（候选按 profile 的 up / down / rangeNavCells 过滤成单向 / 双向，携带连通域间有向可达图）。
- **桥面层**（RT-16 的网格级部分）：桥实体栅格化为桥面格 + 桥头 portal;桥面可走格 = 区域定价 ∩ 净空腐蚀,桥头腐蚀半径内的地面可走格并入;连通域合并 = 桥面洪泛 + portal 格双通合并（与参考实现 mergeTiles 同语义）。
- **高度输入**：`NavHeightField` 从 `.height` 资产的 uint16 样本推导导航格高度与坡度;样本解码先在 double 完成（raw × 分子 ÷ 分母）再转 Fix64——定点域内直接乘会溢出 Q31.32。Web 导出端同样从量化样本重建,两端读同一份输入。
- **对拍**：导出器跑参考实现的真实 `buildNavContext` 产出逐上下文摘要;C# 侧重建同序字节流,FNV-1a 全等（5 上下文 × 3 种子）,连通域按划分规范化比较（不按标签编号）。`JumpLinkFilter_RespectsDownCm` 覆盖跳跃能力收紧后链接减少的验收点。
- **目视**：探针 `--s2` 模式渲染逐体型可走区域（不可走遮罩 + 桥面 + portal + 链接）,同一查看器切换。
- **已知边界**：坡度阈值处的定点 vs 浮点残差实测约 1e-5,三种子均未翻转任何格。

## S3-a 交付：tile 烘焙链与 tile 缓存（本次）

- **移植**（`Nav/Recast/` 与 `Nav/`,顶点与拓扑是整数格角点几何;简化 / 边长阈值比较与格心归属判定用 double,边界见数学段）：距离场（`DistanceField`）、分水岭区域（`WatershedRegions`,含计数排序 / 层级生长 / 小区域合并的完整顺序语义）、轮廓追踪（`ContourTracer`,tile 边界永不简化为墙、格角必留、Douglas-Peucker 整数叉积 + 坐标决胜）、耳切三角化与洞合并与凸合并（`PolygonOps`）、`NavTileBaker`（tile → 凸多边形 + 邻接 + 边界段 + cell→poly）、`NavTileCache`（内容键 LRU,无哈希碰撞）。
- **桥面 tile 与地面 tile 共用同一缓存**,条目是独立内容键;无可走格的类型（如 hull 的桥面）不产生条目——S1 先修了一个 +1 编码双重叠加导致的键错位。
- **对拍**：导出器把五种上下文共享的 tile 缓存逐条目（内容键指纹对齐）落真相,C# 逐字段 FNV 全等——三种子各 769 / 700 / 883 个不同 tile,缓存命中 / 烘焙计数也一致。
- **踩过的坑（已固化成回归测试 `S3RegressionTests`）**：定点 `Sqrt` 在"路径端点格"上的 ~1e-5 误差会吃掉覆盖判断的 EPS——桥 / 路径跨度按生成器约定轴对齐,轴对齐时单位向量与长度取精确值;斜线路径（S7 道路 / 泛洪）届时再定精度口径。
- **目视**：探针 `--s3` 渲染 NavMesh 多边形网格叠加图（`crowd_simulation_s1337_navmesh_foot.png`）。
- **S3 剩余**：走廊（A* + 漏斗）、流场 Dijkstra、路径服务固定生效帧——S3-c 已交付,见下。

## S3-b 交付：全局拼装与 HPA* 抽象图（本次）

- **移植**：`NavMeshAssembler`（跨 tile 多边形拼接 + 边界重叠段缝合为 portal,重叠对按不可变条目对记忆化）、`HpaGraph`（按导航 tile 做入口扫描、tile 内 Dijkstra、逐 tile 不可变块、跳跃链接作有向抽象边、桥面节点键 = 每边导航格数的平方 + 格且只在 portal 跨层 0 代价相连）、`NavGridSteps` / `NavMinHeap`。
- **对拍**：拓扑(节点格 / 邻接目标 / 边数 / 网格多边形结构)逐位 FNV 全等;代价数组按带宽比较,三种子实测最大分歧 ~2e-5(Fix64 与 f32/f64 两条算路的累积差,写进测试断言与文档)。
- **审计**：每阶段交付后跑交叉对抗审计。本轮 A 链(导航烘焙)12 片 + B 链(S0–S2)6 片全绿,唯一实质发现是 tile 缓存少了参考实现的 bakeMs 遥测(已补)与单线程归属契约没写清(已写)。
- **目视**：探针 `--s3` 视图叠加了 tile 网格与入口节点。
- **验收（真机）**：Raylib 宿主经 launcher 预设启动，地图、阻挡物实体与高度图地形（含高程着色）真实渲染，见 `artifacts/acceptance/crowdsimulation-s1/raylib-s1337-live.png`。地形类型着色叠加层在宿主内尚无 presenter，由探针视图承担。
- **顺手的基建修复**：`ContinuousHeightSampleScale.Decode` 的 int32 溢出（大缩放比例资产会触发，10k 资产因恒等比例从未踩到）改为先 widening 再乘。

## S3-c 交付：两点之间的路线（本次）

- **移植**：`TilePathQuery`（tile 寻址的多边形 A* + 跨 tile 缝合 + 桥头 portal 跨层）、`NavFunnel`（简单愚蠢漏斗）、`HpaQuery`（抽象图 A* + tile 内 Dijkstra 起收尾）、`FlowField` / `FlowPool` / `FlowFieldBuilder`（积分场 Dijkstra 限走廊掩码 + 按弹出序拉直 + 路点链）、`CorridorQuery`（corridorTo：无链接先 NavMesh,有链接直走 HPA*,失败回落;桥面起点 BFS 下桥）、`PathQueryService`（固定生效帧路径服务）。
- **对拍契约（两套浮点体系下唯一诚实的口径）**：逐位 FNV 只对真整数——分支选择（NavMesh / HPA* / 不可达）与 trace 是否到达;代价类（integ / 路径代价）紧带宽 5e-6;几何类（折线总长 / len）松带宽——等代价的十字路口择路被最后位差翻转时两解同最优,几何却不同;到达集合 / 路点是浮点派生量,按"独占格必须在走廊分歧的 tile 内或交集洪泛外"与"决定性全等、平局双解皆合法"规则校验。实测三种子 64 对 × 5 上下文:代价最大分歧 2.6e-6,约 497 万到达格路点全等、1475 个平局格全部按规则通过,独占格（三种子合计 1626 个）全部是整 tile 的走廊择路分歧。
- **服务验收（`S3PathServiceTests` / `S3SoakTests`）**：后台线程 1 / 2 / 4 的 320 份答复滚动 FNV-1a 64 逐字节等价（每 worker 独占搜索缓冲,RT-03）;生效帧恒为请求帧 + latencyTicks,后台慢注入时仿真原地等待、答复内容与帧序不变;单次规划超 planTimeoutMs 即故障报错,不重试不换算法;浸泡 10 仿真分钟（18000 帧 × 3000 请求）池 allocated 预热后恒为 2、托管内存平在 26 MB。
- **踩过的坑（已固化成测试或注释）**：链接端未到达时 `integ + cost` 在 Fix64 下溢出回绕成负数、判等即真（参考实现靠 Infinity 语义天然免疫）,加未到达守卫;stitch 记忆化按 uid 做键,跨缓存实例撞号（三个种子连跑才暴露）,改条目引用键;tile 多边形代价备忘挂在 worker 暂存上,跨上下文复用时串味,键补 navId;流场回收全进 0 号 worker 的池,另一池永远得不到还回、无法平台化,改按 OriginPool 物归原主;worker 崩溃曾经悬挂所有在途请求,改为服务故障（报错,绝不静默）。
- **演示**：探针 `--s3c` 渲染折线 + 方向箭头（`artifacts/acceptance/crowdsimulation-s1/crowd_simulation_s1337_s3c_*`）;`CrowdSimulationS3PathMod` 真机演示（`raylib-s1337-s3path-interactive.png`）按 spec 演示契约全部接上：**左键点起点、右键点终点**（光标走 ScreenRayProvider + 高度图射线落地）,**Q 巡回代理体型**（全部去重导航上下文）,**T 巡回后台线程 1/2/4**（结果与线程数无关）,**G 注入后台变慢**（仿真暂停等待,画面不卡,绝不使用半成品）;HUD 实时显示请求帧 / 生效帧 / 当前帧 / 等待次数 / 体型 / 线程数 / 分支 / 可达性。呈现全部走正式基建,不吃调试通道：方向图经新增的**全场投影器注册表**（`GlobalFieldVisualProjectorRegistry`,宿主不再逐个认识场）写进全场视觉缓冲,再由**地形贴花槽**渲染（复用旧 walkability texture 通道:`SetNavWalkabilityOverlayExternal` 外部持有纹理 + Flow 场契约 Vector4 = 方向 + 强度,方向取色相、强度取不透明度,双线性过滤,整张贴花随地形,不再有逐格小方块）;折线与起终标记走新增的**路线视觉缓冲**（`RouteVisualBuffer` + `RaylibRouteVisualRenderer`,gameplay 通道,地形跟随,移动路线 / 阵型槽位后续同路）。调试绘制顺带修了地形跟随（`GroundSamplerM`）。
- **顺手填掉的基建缺口**：宿主场投影块的影响场分支原来错嵌在"离散权属存在"的条件里（本图没有离散权属就不执行,嵌套从合并遗留,这次才暴露）——离散 / 影响 / 注册表三路已拉平到同一层;URI 版 `ClearNavWalkabilityOverlay` 原来每帧无条件清槽,会把外部持有的贴花一起清掉（贴花接线上轮怎么都画不出来,根因就是这个清绑交错）——清除按纹理所有权分流,URI 配置轮询只管自有绑定;overview 简化网格原来一律洗掉 walkability 叠加,Flow 类贴花加了 `NavWalkabilityOverlayVisibleInOverview` 按场类保留;贴花 drape 的逐格小方块路径保留为无连续高度图时的兜底。
- **顺手的输入接线**：点选在固定步读 `AuthoritativeInput` 冻结快照的 `PressedThisTick`，指针位置取同一快照的 `PointerPos`；落点仍是 `ScreenRayProvider` + 高度图 `TryRaycastGround`（高度图在地图装载后才有，不能在 GameStart 抓一次）。命令在会话步进前进入队列；被取代的请求由 `PathQueryService.Discard` 作废（未开工不算、已算完丢弃）。分层视图:V 巡回 路线(方向场贴花) / 可走区域(新增 `GlobalFieldVisualKind.Walkable` 直接 RGBA 场类,不可走红罩 / 桥面棕 / portal 黄) / NavMesh+HPA 线框(多边形描边 + tile 网格 + 入口节点 + 跳跃链接,路线通道共享数组缓存)。

## S4-a 交付：部署单位与指令回放内核（本次）

- **单位 = Arch ECS 实体**（不克隆参考实现的 SoA 存储）：`CrowdSimulationAgent`（速度、个人半径、推挤优先级、special，以及生成时写入的档案下标和移动类型下标；profileId / agentTypeId 留在模板）、`CrowdSimulationUnitState`（槽位 / 组 / state 等校验字段）、`WorldPositionCm`、`PlayerOwner`（Ludots 玩家号 1..P）。管理层 `CrowdSimUnits` 只持稠密序 + 18 位槽位 + 14 位代的句柄池（与参考实现 units 的句柄同构,回放可复现）;`CrowdNavGroupSet` 是（玩家 × 移动类型 × 半径级）的组注册表。
- **单位创建走模板生成管线**：有呈现接线的会话里,单位实体由引擎现成的 `EntityBuilder.UseTemplate→Build` 同步实例化（`RuntimeEntitySpawnSystem.SpawnTemplate` 同款形状,不经生成队列——对拍要求单位在指令 tick 内就位）,模板写真组件与 Mod 作者附加组件经 ComponentRegistry 数据驱动落到实体;仿真参数写在模板的 `CrowdSimulationAgent` 上，生成后不再按配置补速度、半径或推挤优先级。钩子随后补逐实例事实（位置/玩家/仿真身份/呈现预置件）并把稠密序与句柄登记进 `CrowdSimUnits`——分配顺序与改造前逐位一致。模板点名 `profileId` 和 `agentTypeId`，激活时按（移动类型 × 半径级）预解析并校验闭包。两移动类型共用体型时各拿各的模板键。无头对拍与回放没有单独的组件集：测试装配把 CrowdSimulationMod 的模板挂进 VFS，再经同一条 EntityBuilder.UseTemplate 实例化。出生算法（盐流/环形搜索/编组/散布）留在内核;S4/S5 真值测试原样全绿。
- **部署算法逐 op 移植**（sim/population.js 的 spawn / spawnAt）：mulberry32 加盐流（每条指令自己的流,输出 u32/2³² 二进分数,Fix64 精确表示）、出生点抖动 → nearestPassable 环形搜索 → 编组中心 → 三角散布 → 格内 inset 落点。满容量（`sim.maxUnits`）拒收并计数（D54:失败不留半生成状态）。
- **规范校验码（FNV-1a 32,Fix64 原始值口径——甲方体系）**：字段顺序与参考实现 unitChecksum 一致,定点字段按原始 int64 低→高 32 位混合。同一条 Mix 在 `position`、`motion`、`state`、`avoidance` 四处留下断面，合计等于最后的 `avoidance` 断面。诊断断面另起 Mix，不进合计。分区怎么对，见下方「校验码分区」。位置在两端同网格：生成公式在 Ludots 是 Fix64 算术,参考端经镜像侧 S4 归一补丁按同一语义逐 op 求值（FromDouble 向零截断、乘法向 -∞ 取整;**位置乘法在两端都精确,不允许再取整**——米域取整比厘米域少 50 个原始单位,已踩过并固化）。
- **指令队列与回放**（core/commands.js CommandQueue 移植）：指令是数据,tick 边界执行;日志 = 完整输入,回放 = 新会话 + 同一日志。会话的 tick 就是 `Engine/clock.json` 的 FixedHz,不存在第二个频率。
- **对拍（`S4DeployTruthTests`,3 种子）**：同一份脚本（spawn 12000 → 框选 → 点名生成 20 → 全选 → 清空）逐帧校验码与沙盒一致（150 帧 × 3）;12020 个单位逐条（玩家 / 模板 / 半径级 / 组 / 位置原始值）一致;同一份记录回放两次校验码相同。
- **踩过的坑（已固化）**：S0 配置的 `deploy.bases` 玩家 4 出生点 y 写成了玩家 2 的值（0.65 → 0.7 份额,部署差 416 格才发现;组号全对上但中心全错,按"组-中心-RNG 值"三层对拍才定位）;镜像侧量化函数把位置乘法也取整,奇数半档比 Ludots 少 50 个原始单位;参考实现玩家 0 基与 Ludots 玩家号 1..P 的换算只在 `PlayerOwner` 与脚本数据边界做一次,内部编号序不受影响。
- **配置增量**：`world.seed`（生成盐,进覆盖配置）。`CrowdSimulationAgent.special` 为 true 的模板不参与批量部署。

## S4-b 交付：部署演示纯数据化——单位进 presenter 管线（本次）

- **会话宿主下沉 Core**：`CrowdSimulationRuntime`（`src/Core/CrowdSimulation/Runtime/`,MassNavigationRuntime 同款形态）在地图聚焦时按配置目录合并出的 CrowdSimulationConfig + CrowdSimulationDebug 建会话——会话跑在**引擎世界**上（此前演示 mod 在侧世界 `ArchWorld.Create()` 起会话,呈现管线根本看不见单位,这是 S4 演示最初看不到单位的根因）,tick 由系统组的 FixedHz 驱动,会话与运行时注册成服务。S1/S3 演示 mod 的同款 Load 样板后续都往这里收。
- **单位接入呈现投影（不造第二条路）**：`CrowdSimUnits.Add` 在接线存在时给单位挂齐呈现预置件——`PresentationStableId` + `EntityTemplateKeyRef`（模板键来自单位模板 id，按移动类型和半径级映射）+ `PreviousWorldPositionCm` + `VisualTransform` + `CullState` + `ContinuousHeightmapSampleState` + 半径黑板。之后 `PresentationEntityLifecycleSystem` 按 presenters.json 规则数据驱动地建/毁 presenter,与生成队列出生的实体同一条路。无头对拍与回放使用同一套生成接线，呈现预置件照挂；校验码不读这些呈现字段。
- **呈现全部声明式**：形状 = 体型模板键（步兵=方/水军=圆/山地=三角/两栖=菱形(立方体 localRotation 45°)/跳跃=十字(body+orientation 双槽位十字梁),前二用内置原语,三角形是新增的 `unit_triangle.gltf` 数据资产,走 mesh_assets + host_assets 的正式宿主绑定）;玩家色 = presenter `entityColorVector` 绑定 + 新增的 `TeamColorPalette` 服务（`deploy.bases[].color` 是配置数据;`PresenterBehaviorSystem` 优先读调色板,未注册回退原双色解析,旧场景零影响）;选中环 = select 指令执行时镜像进实体集合仓的 `selected` 集合,presenter 监听 `EntityCollectionMemberAdded/Removed` 建/毁选中环（GroundOverlay Ring,`scaleParamKey` 绑单位半径黑板随体型缩放）。校验码不含选中,镜像不进对拍口径。
- **演示 = 数据脚本**：S4 演示 mod 删成纯数据（无 main）——`CrowdSimulationDebug.json` 声明 `session.autostart` + 一段与对拍资产同构的脚本（spawn 12000 → 四个基地外,另在地图中心给四个玩家各点一团 300 单位的展示生成,让镜头够得着）+ `autoReplayAtTick:150` 自动回放。真机日志：13220 单位全数落地、20 个体型 presenter 定义各数百实例激活、首单位视觉变换贴在地表（446.7 m）、回放逐帧对拍一致（status=1）。
- **踩过的坑（已固化成代码注释）**：① 缺 `PreviousWorldPositionCm` 时 `WorldToVisualSyncSystem` 的查询直接捞不到单位,`VisualTransform` 永远停在默认值,presenter 全沉在（0,0,0）;② 缺 `ContinuousHeightmapSampleState` 时地形高度同步不写 Y,单位埋在海平面以下的地表里（探针打印首单位 vt 才看见 Y=0）;③ 引擎全局档案注册表是多特性合并产物,Core 的小体型（r20~80）并进来会把半径级挤歪、`NavFor(1,0)` 直接缺键——会话只物化单位模板点名的档案，不把注册表里其余体型当成半径级;④ presenter 配置的 `extends` 合并到行为槽位为止,`assetBinding` 是整换不并字段,变体干脆整段写全;⑤ 行为槽位有注册表,`cross` 的两根梁用 `body`+`orientation`,不能自造槽名。
- **相机**：S4 mod 用同 id 片段把演示相机的 `edgePanMarginPx` 关成 0（无头跑证据时指针静置在窗口边缘会把镜头慢慢拖走,抓屏全都对不上）。
- **选中链的目标形态（S8 前记档）**：终态 = rep entity 挂交互上下文声明 `activeCollectionKey`、选中落在玩家本地集合、下令命令自带成员清单（不再从选中集合反查），`select` 指令随之退出指令日志——选中是本机 UI 态,不是仿真输入。当前实现是 select 指令 + 会话实体自声明键（`selected` 集合,SelectionMirror 镜像）,与输入配置宪法的自声明键口径一致,已合规;换轨在 S8 正式下令链接入时一并做。

## S5-a 交付：移动内核与阵型（本次）

- **移植**（`src/Core/CrowdSimulation/Movement/`,对照参考实现 `movement/{leaders,intent,motor,flowSample,contact,walls}.js`、`orders/order.js`、`formation.js`、`planning/planner.js`、`sim/issueOrder.js`）：`CrowdLeader`（虚拟领队：前视转向 / 限速 / D61 镜像 / D62 朝向继承 / SetStart）、`CrowdIntents`（槽位 ↔ 流场 blend、LOS 滞回、D59、车道展开、D60、停滞触碰到达、LY-5 桥头层切换）、`CrowdMotor`（一阶响应 / 转向限速 / 速度上限 / 墙面滑动 / 跳跃）、`CrowdFormations`（assignSlots / relativeSlots / layoutSideBySide）、`CrowdOrderBook` + `CrowdIssueOrder`（magic box chooseMode、D57 去重、重组）、`CrowdSimPlanner`（plan / applyDue / finishOrder,走 S3-c 的固定生效帧路径服务）、`CrowdSpatialHash` / `CrowdWalls` / `CrowdContact` / `CrowdBlockerColliders` / `CrowdFlowSample`。
- **会话接线**：`CrowdSimSession.Step()` 返回 `string?`——规划答复未回时返 null,引擎让出本帧（`BlockOnDueReplies` 区分真机让出与无头同步等待）;`CrowdSimulationKinematics` 恒挂;校验码运动字段从占位零改读真值;`NavContext` 收编 `CanReach`（LY-4：上层编号接地面顺排 + 桥头互通边）,`NavLinkSet` 退回纯数据。`order` 指令进 `CrowdSimCommands.Exec`。
- **对拍（`S5TrajectoryTruthTests`,600 tick × 241 单位,种子 1337,crowd 全量 75/75）**：状态机字段（state / mode / level / order）全程逐位全等;位置是浮点派生量,按逐 tick 带宽比对。初校实测 p99 = 2234 cm,当时判为等代价隘口的合法平局翻转并据此定带;静态审计抓出马达转向限速乘法溢出（P0,修复于 a83d929585）后复校,p99 = 0.17 cm、max = 0.66 cm、末 30 tick max 0.17 cm——大分歧全是溢出所致,带值收紧为 `MaxTrajectoryBandCm = 100`、`FinalBandCm = 10`（各留约 150 / 60 倍余量,内核回归以米级偏差触警）。两端当时挂同一屏蔽开关 `__S5_NO_AVOIDANCE__` 关避让推挤（哈希照建,到达与接触语义不变）;该开关随 S5-b 摘除,避让成为内核固有行为,带值同步按避让开启口径重测（见 S5-b 段）。真值资产 `s5-trajectory.bin` + 脚本 JSON 进 S1Terrain1337Mod 的 parity 目录。
- **踩过的坑（已固化成代码注释或测试）**：① Fix64（Q32.32）的平方在厘米域约 463 m 就溢出——领队 aim / step、拉直路点方向、槽位距离、chooseMode spread 全部中招（山地领队曾因此瞬间走完全路径、路点方向曾全零致单位卡死）,`CrowdFix.Hypot` 先按最大值归一再开方,厘米域长度一律走它;② 碰撞半径口径是**个人半径**（radiusCm × avoidanceRadiusScale = 600 cm）,不是导航半径 100 cm——墙面滑出 / 接触 / 阵型占地 / 空间哈希环统一读 `ProfilePersonalRadiusCm`（真值里 u173/u174 钉在面 − 600 实锤）;③ 马达静止分支的跳过条件是 `rest && calm && 全零`——S5 关避让后 calm 恒 0,静止单位照过马达,压住被挡格时的墙面滑出是真实位置来源;曾误加成全零即跳,已还原（S6 接 calm 时再评估是否恢复跳过）。
- **真机演示**：S4Deploy 演示相机补 `targetSource: Fixed` + `fixedTargetCm`——此前 `VirtualCameraRequest` 只带相机 id,轨道目标沿用地图默认相机位姿（世界中心、19.2 km 视距）,12000 单位全程在画幅外;Fixed 位姿是 `VirtualCameraBrain` 的现成激活语义,零代码。视距定在 400 m：r400 单位（8 m 方块）约 7 px,阵型间距与玩家色可读,`allowUserInput` 保持,可自由缩放平移跟随行军。验收图 `raylib-s1337-s5move{4,5,6}.png`（1.5 km 行军带 / 800 m 阵型散布 / 400 m 单位特写）。
- **审计**：交叉对抗审计 A 链 8 片（leader / intents / motor / formations / orders / planner 两片 / support）+ B 链 3 片（会话停摆 / 导出契约 / 运行时指令）全绿,托管 deepseek-v4-pro;spark 抽查 s5-intents 空响应（已知不稳,未计入）。两片附注确认审计真实读码：motor 片的 metrics / calm 裁剪与 support 片的定点等价表达均被识别为声明过的移植口径,不计缺陷。
- **S5 剩余**：拖线摆阵演示（走 S3-c 路线视觉缓冲同一条路）、走廊扩展 processPending、多起点 D08 规划;最小避让已随 S5-b 交付,态度模式推挤与阻挡盒推挤仍归 S6。

## S5-b 交付：最小避让（本次）

- **移植**（`Movement/CrowdAvoidance.cs`,对照 `avoidance.js computeSeparation` 全 13 条语义）：哈希序聚集(腾空 JUMP 半径记 0)、逐格唤醒标记与静态岛跳过、只同层相碰（LY-6）、同组双动对称对半、功能位推挤（Yield:低者让 1−dominantShare / 相等对半 / Hold:休息单位对移动单位不可动）、双预算（maxNeighbors 数重叠对,maxScan 数所有读到的候选,D07）、查询范围 = 单位自己的哈希环、格剔除（边格永不剔）、完全堆叠对的确定性伪随机反对称轴（下标对派生,绝不同向）、接触边界连续的分离式、calm 休眠、maxPush 截断 + 时间平滑、stride 行错峰降频。玩家对矩阵现在由会话开始时的 `pairRules` 投影写入，装载函数只接收这张表；作者配置见上面的「关系功能位」。推挤优先级写在单位模板的 `CrowdSimulationAgent.pushPriority`，装载原样使用。
- **内核接线**：子步序与参考 `tick()` 一致（哈希重建 → 分离求解 + 相位前移 → 领队 → 意图 → 马达）;stride 按参考 setRates 推导（30 Hz × 1 子步 ÷ avoidHz 15 = 2,行错峰）;马达接 sepW 权重并恢复 rest∧calm∧全零 的休眠跳过;意图层补 ARRIVED∧calm∧零速 直接休眠门;spawn 清推挤与 calm（D54）。
- **屏蔽开关摘除**：两端 `__S5_NO_AVOIDANCE__` 同步删除（导出器与沙盒引擎）,避让成为内核固有行为,会话与内核不留开关位。
- **L02 校验码重钉**：运动字段（vx/vy/slotX/slotY/blend/stall）从"每字段混一个零词"改为按原始 int64 低 32 → 高 32 两词混入——导出端 canonChecksum（mixI64）与 `CrowdSimChecksum` 同步;部署会话无运动栈,两词恒零,真值变化来自混法本身。S4 真值三种子重导,150 帧逐字全等。
- **对拍（`S5TrajectoryTruthTests`,600 tick × 241 单位,种子 1337,crowd 全量 77/77）**：状态机字段全程逐位全等（0 处不一致）;位置带宽按避让开启口径实测——p50 = 0.07 cm、p99 = 17.3 cm、max = 136.1 cm、末 30 tick max = 35.2 cm,带值定 `MaxTrajectoryBandCm = 500` / `FinalBandCm = 100`（3.7× / 2.9× 余量;结构级分歧照常以米-公里级偏差触警）。**L24 分层门**:p50 ≤ 0.5 / p99 ≤ 50 / p99.9 ≤ 100 cm(实测 0.07 / 17.3 / 35.1,余量 7.3× / 2.9× / 2.9×)+ 沿航向有符号均值 ≤ 0.1 cm(实测 0.0224,余量 4.5×;系统性偏置探测门——全体单位同向漂移时 max 抓不到、均值抓得到,航向基 = 参考端逐 tick 位移);max/末态维持 500/100。位置尾差定性见下方整改小节(L21 修订):来源记档为参考端 f32 存储。
- **踩过的坑（已固化成代码或注释）**：① 堆叠伪随机轴是米口径,厘米域须 ×100,漏乘则堆叠推力量级差 60 倍;② sep 是无量纲公式值（两端同数）,restDeadband/maxPush 阈值必须与参考逐字同数,按"米→厘米 ×100"换算会把休眠判定错 100 倍、单位集体睡死;③ 空间哈希的 reach 与格距必须用最大**个人**半径（2400 cm → 格 4800）,误用导航半径(400)时 S5-a 无预算路径侥幸等价,预算裁剪（maxNeighbors/maxScan）下接触子集即分叉。
- **真机**：12000 单位上屏,行军/到达区无叠点（避让可视）,tick 速率无明显劣化;截图 `raylib-s1337-s5move9-avoidance.png`。

### S5-b 整改(对抗审计 L11/L12/L13/L14)

- **L11 推挤矩阵下标**：聚集缓冲改存玩家**表序下标**(`CrowdPairRules.IndexByPlayerId` 随运行时配置进求解器)——Players 不按 1..P 顺排时查表仍对齐,PlayerId 超 255 不再被 byte 截断。玩家号仍须是 1..P 的排列。玩家对级 overrides 已删除，装载不再按队伍号生成矩阵。
- **L13 哈希几何精确化**：格边长与触及距离改定点精确值传入(此前经 int 截断;参考端是浮点 inv=1/cellSize),格归属的向零截断、环数 ceil 语义与参考逐字对齐。默认配置下格距 4800 cm 本就整除,修复对演示场景是潜在正确性(非整除配置才触发),不改变现测轨迹。
- **L12 定带证据与定位结论（L21 记档修订）**：首个分歧点:tick 11、单位 163、马达静止停车分支——n2 = 1.8916(C#)/1.8904(参考) 对 slow2 = 1.8906,差 5e-4,两侧输入(sep/pos/速度)一致到 1e-6;骑线翻转停车分支,离散事件再放大成位置尾差(与 L13 不同源:L13 是潜在正确性修复,未改变现测轨迹)。输入差来源定性为**参考端 f32 状态存储**(位置量化格 2^-10m;生成位置全精度,首次马达步即落格)。去量化实验(f64 化参考端 units 位置存储与避让聚集/输出缓冲,临时实验已还原)后首个离散分歧不消失——当时据此判"尾差不是 f32 量化噪声单独所致";L21 修订:该实验未覆盖全部 f32 路径(restX/Y、slot、blend、stall、jump*、motor.js 的显式 fround),不收敛不构成反证,此判撤回;Fix64 每运算舍入约 2^-32 cm,比观测差小约 10 个数量级,排除为主因。带值维持 500/100,不允许再放宽;结论与 2026-10-09 甲方裁决(行为对拍口径)不变。
- **L12-④ 接触计数硬门**：真值 bin 每单位追加 u16 接触计数(每 tick 清零、求解行累加、跳过行保持 0,两端同构逐位可比;体积 +289 KB)。实测 600 tick × 241 单位不一致 10/144234 样本(0.007%,全部 ±1 骑线翻转),硬门取双容忍线:不一致样本 ≤ 0.05% 且单样本幅度 ≤ ±1——系统性求解分歧(接触集大面积错)会同时击穿两条。
- **L14 避让隐藏状态进校验码**：校验码追加分离两轴 + calm 逐单位(mixI64 双词)与相位一词(逐 tick),导出端 canonChecksum 同步(部署会话无运动栈,新字段恒零,词数对齐——注意块序在整型字段之后,与 C# 同序);S4 真值三种子重钉,150 帧逐字全等。**回放口径写明:回放从 tick 0 起新会话**(`CrowdSimSession.Reset` 后逐帧重演,快照恢复不走中间态)。**Stride 写明:只在会话创建时按 rateHz 推导一次,Ludots 端不支持参考端 setRates 的运行时调频。**

### 校验码分区

合计仍是一条 FNV-1a 32（`CrowdSimChecksum`）。`Compute` 沿这条 Mix 记下四个断面：`position`、`motion`、`state`、`avoidance`。后一个断面从前一个的结果接着混。`Total` 等于最后一个断面 `avoidance`。四个分区按这个顺序接完，合计就是这条 Mix 的终态。`ComputeValue` 返回 `Total`。S4 真值与回放只认这个合计。`S4DeployTruthTests.DeployAndChecksums_MatchWebTruth` 和 `Replay_TwiceProducesIdenticalChecksums` 把 `Step` 交出来的 `Total` 和真值帧逐字比对。

诊断断面另起三条 Mix，域词分开，不写进 `Total`。它们只把分歧指到子系统。词可以随定位需要改，不冻结成真值。开关开着和关着，同一段推进的 `Total` 与 `position` 相同。`CrowdSimSession.DiagnosticDigest` 默认关，三个诊断词为 0。打开之后每个成功 tick 都算。`CrowdSimulationRuntime` 在自动回放已武装（`autoReplayAtTick` ≥ 0）时从第一 tick 打开，回放会话复制同一个开关。50k 计时门走默认关：`jump` 要再扫一遍全部单位。

合计断面里的词：

- `position`：本 tick、单位数，然后所有单位的 x，再所有单位的 y。定点按原始 int64 先低 32 位、后高 32 位。
- `motion`：vx、vy、slotX、slotY、blend、stall。一个字段走完所有单位，再下一个字段。
- `state`：state、group、句柄、order、mode、level。到达写在 state 里。句柄里是槽位和代。单位当前挂着的 order 号在这一段。
- `avoidance`：分离的 x、y，calm，最后相位一词。没有运动内核时这几词是 0。

诊断断面（开关打开时）：

- `groups`：按组槽走，空槽记 0。活组记 `OrderId`、`Goal`、`GoalComp`、`Planning`、`StateSeq`、`LosAll`。领队和上一任领队记位置、朝向、路径上的每个路点、当前路点下标、是否走完。流场记 `BuildRevision`（缓冲每成功构建一次加一，池内复用继续累加）、`NavId`、`Goal`、`Reached`，以及目标格上的长度。后面接指令簿（id、玩家、模式、去重键、组链接）和在途规划（到期 tick、组目标、请求号、导航版本、刷新队列的组号）。
- `jump`：每个单位的 `RestCm`、`JumpToCm`、`JumpT`、`JumpLengthCm`。没有运动内核时读组件；缺组件则这六词为 0。
- `commands`：未执行条数，再按队列顺序混每条的 tick、种类和载荷字段（坐标、数量、形状串、模板串、区域顶点）。`Flush` 只在执行时写日志。回放输入是已执行日志，再接上尚未到点的队列。

`CrowdSimChecksum.DiffDigest` 同时给出两样。合计的第一处分歧：tick 与 `Section`（position、motion、state、avoidance 的第一个）；没有则为 tick -1、断面名为空。诊断的第一处分歧：`DiagnosticTick` 与掩码；没有则为 tick -1、掩码 0。两样可以落在不同 tick。后段合计接续前段，更早的合计词变了，后面的合计断面会跟着变，函数只报第一个合计断面。共同前缀的合计一致但条数不同时，合计 tick 为 -2，section 为空。

`ReplayStatus` 只看合计。合计相同记成 1，`CrowdStateDigestTests.PendingFutureCommand_DoesNotFlipReplayStatus_DiagnosticLeavesTotalAndPosition` 断言的就是这个 1。2 是合计分歧或条数不同。诊断断面不改这个结论。日志和 HUD 另报最早的诊断分歧，形如 `diag differs: groups`，并带上那个 tick。掩码为空则不附。

开关关闭时诊断词恒为 0。对拍两边要用同一个开关，否则诊断断面会整段报差。

下面这些仍会影响下一 tick，合计和诊断都不收：

- 体型上的速度、个人半径、推挤优先级。生成之后不随 tick 改写。本 tick 改它们，下一 tick 的运动或避让才会露出来。
- 流场每格的下降方向。摘要里只有构建代次、目标格、到达格数和目标格长度。
- 迷雾格、认知槽、重烘任务、下一次生成用的随机流序号。导航格本身也不在摘要里。
- 选中标记。它是呈现态，校验码不读。

空间哈希、意图缓冲、接触计数、开放格缓存、组级备忘，都从已经入摘要的状态或导航版本重算，不单独成段。接触计数每个 tick 清零。开放格缓存按导航版本失效。组级备忘在每次收拢时作废。

## 结构变更约定（L35 对齐 ecs_soa §5）

引擎合同（`docs/architecture/ecs_soa.md` §5）：结构变更（Create/Destroy/Add/Remove）禁入查询热环，
要么记入 Arch `CommandBuffer` 在阶段尾 Playback，要么走 `RuntimeEntitySpawnQueue`/
`RuntimeEntityLifecycleQueue` 由 EffectProcessing 组的系统物化。人群侧的落点：

- **spawn**：单位生成在 tick 边界的指令冲刷点（`CrowdSimulationSessionTickSystem` → `Step()`
  首行 Flush）同步执行——这是已记档的对拍豁免（单位必须在指令 tick 内就位，真值行序 = 稠密
  创建序）。生成不发生在任何查询环内,与 §5 的「禁入热环」一致;模板道 `EntityBuilder.UseTemplate
  → Build` 一次成形,仿真参数在模板组件与生成后钩子里落齐,不在 Create 之后逐组件 Add。
- **kill**：当前内核没有 kill 指令（参考端 killAt 未移植,缺口清单在案）。未来引入时的合同:
  tick 边界指令点入队 + 呈现生命周期两帧销毁（有 `PresentationStableId` 的走
  `PresentationEntityLifecycle.RequestDestroy`）,稠密销毁记 CommandBuffer 一次 Playback。
- **Clear/重部署**：当前 `CrowdSimUnits.Clear()` 对模板道单位直接 `World.Destroy`,不经过
  呈现生命周期——已记为已知缺口,与呈现接线改造同轨补齐（呈现实体两帧销毁后再重建稠密表）。

## 热路径数据面：chunk 收拢 → SoA → 写回

子步的地址是稠密下标。空间哈希的桶、分离、接触计数和并行区间都按下标读写，还要随机读邻居。Arch 的查询按原型分块，一块 chunk 盖不住整张稠密表，查询顺序也不是稠密序。单位模板的组件集可以不同。以前把原因写成「无头和模板两条生成道、两个原型」。无头那条生成道已经不在，这个说法作废。剩下的原因是：稠密表是创建序，槽位经 `_sparse` 映回；chunk 序是原型序；中间没有一张跨 chunk 的连续 Span 可以拿来做下标划分。

每个 tick 用缓存的 `QueryDescription` 把单位列扫一遍，按槽位映回稠密下标，收成 SoA。子步只读写这张表。tick 末按同一下标写回 chunk 列，不对单个单位做 `World.Get` / `World.Set`。迷雾盖章读的是刚写完的位置和玩家下标，不再按实体回查。指令和结构变更都在这次收拢之前。子步中间没有别的读者要看组件。校验码在 tick 末读 SoA。没有运动内核的部署会话仍直接读组件。意图层的指令簿线性扫和认知槽解析仍是组级备忘，只在 tick 边界失效。规划发生在收拢之前，它改的是组件，下一次收拢能看见。

50k 单位 tick 门（种子 1337、按 deploy.bases 生成 50000、四座基地各下一令远点行军、预热 40 tick、测量 200 tick，`CrowdKernel50KBenchmarkTests`）。Release，迷雾不冻结。计时是推进成功的 `session.Step`，含运动、迷雾刷新、认知同步和校验码。硬门只在优化构建上生效：P95 < 33 ms。Debug 记下数字，然后记为未测。并行工人 4，迷雾刷新 60 次。开测时读 1 分钟 load；高于核数就每 5 秒再读，最多等 180 秒，到期仍高也开测，不重跑。

2026-10-12，4 核。开测 1 分钟 load 1.76，核数 4，等待 0 ms：平均 25.80 / P50 25.26 / P95 30.46 / 最大 31.86 ms。数字在 `artifacts/benchmarks/crowd-kernel-50k-tick/`。

更早的 Debug、迷雾冻结、gather 改造对照：改造前平均 333.1 / P50 332.8 / P95 372.3 / 最大 447.2 ms，改造后平均 231.5 / P50 231.6 / P95 277.8 / 最大 319.6 ms。那组不是现在这道门。

## S7-a 交付：结构动态化内核——建造/拆除/寿命 + 脏 tile 增量重烘 + HPA 增量 + 队伍反应（本次）

- **结构仓**（`Structures/CrowdStructuresStore.cs` + `CrowdStructureFootprint.cs`,对照 `structures/store.js` + `footprint.js` 逐 op）：足迹三形(rect 半边长 / disc 半径 / path 带端点条带,厘米域 Fix64,`CrowdFix.Hypot`,path 端点 ε=1e-4 cm 容差)、bbox、格矩形、格心 covers;仓 = 纯组件表(模板 / 足迹 / NavArea / blocker / 寿命),id 单调递增按 id 序遍历保证确定性;**地图静态阻挡物以实体身份入仓**(与参考 mapGen 同形,动态 op 与其共用一套栅格重标);`rasterRect` 按变更格矩形重标区域(优先级高者胜、同级后放置者胜)与阻挡(逐轴覆盖份额乘积 ≥ blockCoverage,与 `SurfaceGrid.Build` 同式);blocker 集变化即整体重建碰撞 CSR(注册格矩形外扩 reach);逐点查询取最上层实体;寿命到期队列按绝对 tick(`placeTick + ceil(lifetimeSec × FixedHz / simSecondsPerTickSecond)`，decimal 有理数；30 Hz 且每 tick 秒 10 仿真秒时，4 秒仍是 12 tick)。
- **指令面**:`placeStructure` / `removeStructureAt` 进 `CrowdSimCommands`(厘米单位,模板按 id 查配置表,指令即数据 tick 边界执行,脚本化对拍用);`structures.templates` 进配置四件套(DTO/Loader/Runtime/Validator,规则与参考 config.js 同源:footprint 枚举、blocker⇒rect、area⇔priority、area 存在性、priority ≥0、非阻挡且无 area 即无效实体、lifetimeSec>0、id 唯一);**分层实体(桥)的动态放置显式拒绝**(C# 上层动态烘焙未随本单,拒绝不是静默忽略,有测试钉住)。
- **增量重烘**（`Nav/CrowdNavRebake.cs`,对照 `nav.js rebakeNavRects` + `finish`）:变更格矩形 → 逐上下文重分类(0 无变化 / 1 仅代价 / 2 可走位翻转)→ kind=2 时 `erodeRect`(切比雪夫可分离两遍,矩形外扩腐蚀半径)→ 脏 tile 重烘(`NavTileCache` 内容键命中即复用,条目引用相同的 tile 其 HPA tile 内距离逐位复用)→ 连通域全格重标(与全量烘焙同一份洪泛,划分等价;编号不是合同)→ 桥面合并 → 链接(跳跃候选端点触矩形才整表重建,否则按新标号重导出域图)→ `HpaGraph.Update`;kind=1 时 tile 多边形与 HPA 块代价现读重算。导航版本 cost/passable 各 +1,可达备忘随重烘失效。
- **HPA 增量**（`HpaGraph.Update`,对照 `hpa.js updateHpa`）:脏 tile 触到的共享边界各重扫一次,入口表(含桥面跨界表)变化的 tile 重烘 tile 内边,受影响 tile 的块整体重建;**tile 内快路径即 hpaLocal**(邻接表 + 只到目标即停的 Dijkstra),keep 语义(tile 不在脏集或脏 tile 内容未变)下旧距离逐位复用;D23:重生成的跳跃链接落进干净 tile 时其端点表变化也触发重烘。等价基准 `HpaLocalEquivalenceTests`:真实 s1337 上下文上全部 tile 的全部 tile 内边,快路径距离与 `HpaQuery.TileDijkstra`(逐格推导)逐位相等 + 完备性(可达者必在表)。
- **DB-03 增量 vs 全量一致性检查**:`CrowdNavRebake.VerifyAgainstFullBake`(调试开关,`CrowdSimSession.VerifyIncrementalNav`,runtime 由 CrowdSimulationDebug.json 的 `session.incrementalNavVerify` 驱动):每次结构变更后与同栅格全量烘焙逐字段比对(代价/可走/可通行/连通域划分规范重标/格表/链接表/HPA 扁平视图),不一致即抛。对拍免谈(全量重烘即基准),S7 真值测试开启随路跑。
- **队伍反应**（`CrowdSimPlanner.ReactRebake`,对照 `planner.js reactRebake` + `react` + `reachLost` + `leaderPathBlocked` + `onRemainingRoute`）:目标被挡 → 重规划;行进中且变更落在剩余路线上(走廊掩码触脏 tile 且脏 tile 内有 ≤ 成员剩余路线长的格——只看掩码会把身后的变更算进来)→ 领队线四角探边被切断(`leaderPathBlocked`,upPass 托桥)或成员连通域失达 → 重规划,否则**同走廊流场刷新**(`PathQuery.CorridorMask` 路径服务按 padMask ∪ 原掩码建场,`refreshLatencyTicks` 生效,答复按导航版本判陈旧、陈旧重排队;每 tick 预算 maxRefreshesPerTick);F-5:在途规划撞上重烘 → 落帧按现行导航裁决(afterStalePlan)。唤醒 `wakeNear`(各自上下文腐蚀半径 + 自身 + 最大个人半径,量到矩形)与挤离 `evictBlocked`(变更矩形外扩 2×最大腐蚀半径,格心落位,跳跃单位改落点不挤脚下,无处可放计 stuck)在重烘阶段 1 执行。
- **重烘切片(RT-04)**:阶段 0(指令内:仓变更 + 真相重烘)→ 阶段 1(队伍反应 + 唤醒 + 挤离)→ 阶段 2(重规划 + 报告发布),按 `planning.rebakeSlices`(=3)摊到后续 tick;每逻辑 tick 至多一步,任何新指令先收尾未完 job。**RT-05 的 C# 同源实现**:路径 worker 与结构变更共用同一份导航态,经 `PathQueryService` 的导航互斥锁串行(op 应用与在途 Compute 互斥)——任何计算恰好看见其请求前生效的全部 op,时序不进结果;参考端用 worker 私有镜像 + 消息序达到同一契约,C# 单份状态省镜像内存(与参考的生命周期差异,按参考语义为准调整,已验证:同脚本回放校验码逐位一致)。
- **对拍(导出器 S7 块 + `S7RebakeTruthTests`,360 tick × 120 单位,种子 1337,crowd 全量 84/84)**:脚本 = 部署 → 行军 → 建筑挡路(140m,格心位)→ 拆除 → 寿命路障(4s,12 tick 到期)→ 道路(仅代价);**每次 op 的重烘焙报告逐字段硬门**(执行 tick / 类别 / 报告 tick / tiles / contexts / costOnly / 缓存 hits/misses / 重规划指令数 / 刷新队列长 / 挤离数 / stuck / 受影响 tile 并集升序),5 笔全字段一致——命中率、脏 tile 集与反应判定全程同构。逐 tick 门:state/level/order **逐位**(结构字段零容忍);mode 双容忍 0.15%(实测 13/43200 = 0.030%,全部单字段翻——重规划落帧时槽位从漂移位置重排,槽位视线判定的骑线翻转,与 S5 首分歧同源);contacts 硬门改在**位置对齐样本**( |Δ| ≤ 1cm)上成立(0.003%,±2 幅度)——位置分叉后邻居集合本就不同,接触计数无合同;位置带维持结构性兜底档(实测 max 2407cm / 末 30 tick 1259cm,带值 15000/3000 不收紧;整组卡墙类结构分歧在数百米量级仍击穿)。**L22 补行为门**(位置带 150m 拦不住"绕错路"级分歧,降为结构性兜底):每单位终态(state/level/order)逐位、首达 tick 分布直方图(含从未到达桶)两侧相同、阻挡格停留数为零(逐 tick 查两侧位置;放置 op 当帧末态豁免——挤离在重烘阶段 1 = 放置后下一 tick 落地,evicted/stuck 已被 ops 报告钉住;跳跃中单位离网直线可越阻挡、桥面层不查地面阻挡格)、不可达集合(终态 Unreachable)逐位、重规划次数(ops 报告 orders 字段)。**p50 分层门** ≤ 0.5cm(与 S5 同带;实测 0.069)。原 25cm 临时带的成因已定性并修复:放置当帧马达的亚格墙触推离依赖开放格缓存,缓存只按 walk 数组引用失效,而重烘对 walk 是原地腐蚀——放置帧读到旧开放表,推离与参考端不同值,p50 被抬到 8cm 档;补上版本失效(与参考端 openCells 同款)后回到固有误差档。contacts 幅度上限维持 ±2——实测唯一不一致样本(1/36989)幅度 ≤2,判为同 tick 两邻居各翻一次的骑线叠合,收紧到 S5 的 ±1 无实测支撑。
- **真值口径**:参考端以 `__S7_TRUTH_NAV__` 镜像补丁冻结迷雾(fog.update / syncBeliefs 短路,`__S4_FIX64_SPAWN__` 同款门,仅真值导出置位)——S7-a 内核无迷雾(F02 另单),组全程停在 truth 导航,队伍反应 = reactRebake 真值直反应;不做此冻结,op 后组掉进 belief 变体槽,反应经 retarget 走 belief 路径,报告 orders/refresh 与无雾内核结构性不可对齐(已实测)。足迹尺寸取静态图同档(35–140m)、中心落在格心,覆盖份额 0/1 无骑线。
- **踩过的坑(已固化)**:① 碰撞索引必须跟随仓的活 CSR——放置重建出**新实例**,会话持旧实例则墙内单位永推不出(首个真分歧,tick 31 单位 92 差 43m,`Blockers` 已改为派生属性);② worker 复用的 tile 多边形代价备忘键须钉**条目引用**((navId, tile) 键在重烘换条目后越界,worker 故障);③ 寿命 tick 整数契约走参考端 f64 公式(见上);④ 刷新队列长在阶段 1 末快照(阶段 2 读实时值已被每 tick 预算排空,参考端同点)。
- **真机**：S4Deploy 冒烟脚本追加建造/拆除/寿命/道路四 op(12000 单位):5 笔重烘报告全字段落日志(含到期拆除与 costOnly=4 的仅代价重烘,缓存命中计数与挤离计数在案);**autoReplay 回放无分歧(status=1, divergenceTick=-1)**——回放会话用烘焙输入重建仓 + 全新导航 + 独立 tile 缓存(共享活会话已被 op 改写的导航上下文必然分歧,首个分歧恰在首个规划落地 tick 43,已实测并修复);截图 `raylib-s1337-s7rebake-kernel.png`。
- **L15 处置**:本单未触及单位删除路径(evict 是传送不是删除,无 kill 指令移植),swap-remove 不搬推挤状态的问题**仍挂起**,留待涉及单位删除的单。
- **F03-b 演示交互(已交付)**:S3/S4 是资产 mod（无 main）。视图、HUD、建造预览和交互在 Core 的群体呈现里，由各 mod 的 `CrowdSimulationDebug.json` `presentation` 驱动；会话仍由 `CrowdSimulationRuntime` 在地图聚焦时激活。键鼠读 `AuthoritativeInput` 的 `PressedThisTick`，`Submit` 在会话步进之前。**HUD**(ScreenOverlayBuffer,数据全部读内核现成状态):tick / 单位总数与状态分布(闲/行/达/不可达/跳跃)/ 最近一笔重烘报告(exec→report tick、类别、脏 tile、缓存命中、重规划、挤离、stuck)/ 回放结论 / 按键提示。**V 巡回分层视图**(S3 同一通道):路线 = 主力组(成员最多的带流场组)流场贴花 + 目标标记(GlobalFieldVisualProjectorRegistry)、可走区域遮罩、NavMesh+HPA 线框(路线通道,按(导航,重烘序)缓存——重烘换 tile 后自动重建);**重烘脏 tile 闪烁**(最近报告的受影响 tile 并集轮廓,2.5s 线性淡出,路线通道)。**B 建造 / X 拆除 / 1-2-3 切模板**(building/s7barrier/road,尺寸与演示脚本同值;道路自光标向 +X):足迹红绿预览(光标处无既有结构且包围格无阻挡 = 绿,只读结构仓现成数据)、指令走正式指令流(placeStructure/removeStructureAt,固定步入队并在 PostMovement 的会话步进前消费,回放同构);世界落点 = 冻结快照 `PointerPos` + `ScreenRayProvider` + 高度图射线。**交互与自动回放**:首条现场指令进入会话即调 `CrowdSimulationRuntime.SuppressAutoReplay()`(只拦自动触发,RunReplay/对拍语义不动),HUD 显示"回放已停用(交互模式)";无交互时原行为不变(实测自动回放 status=1)。真机截图 `raylib-s1337-s7demo-{hud,build,rebake}.png`(建造/重烘两帧经 AgentBridge 合成输入与按帧截图编排,`scripts/acceptance/run-crowdsim-s4-f03b-shots.sh`)。
- **F03-b 待办**:动态分层(桥)的上层增量烘焙;参考端 stray 走廊扩展(Ludots 侧 OnStray 仍空挂)。

## S7-b 交付：迷雾 / 视野组 / 认知变体内核(F02,本次)

- **视野组与迷雾状态机**(`Fog/CrowdFog.cs`,对照 `fog/fog.js` 逐 op):`sharesVisionWith` 双向成立的玩家并成组(union-find,根取小索引,合组传递);粗格（`FogCellsPerSide = ceil(NavCellsPerSide / NavCellsPerFogCell)`，s1337 上每边 64、`fog.cellSizeCm` = 25000）上 visible 按 fog.rateHz 重建(组 round-robin 摊周期、单位只在本组槽位 tick 盖章,D37 双向夹紧)、explored 粘滞;belief = 实体 id → 末次所见快照(初始地图实体全员已知、见即知、拆除留残影直到地面再见);key = 认知实体集的交换 XOR 哈希(CrowdFog.Mix,JS Math.imul = C# unchecked 同位);BV-6 计数器(entRev/rev/truthRev)做精确变更检测,哈希碰撞藏不住变更。D50 面命令 reveal / obscure / forget / fogShare(并组,拆组不支持同参考端)+ fogSight(LOS 开关);order 的 fogTerrain 选项切乐观迷雾(未探索 tile 按可通行假设)。形状解析与格化(`CrowdFogArea`,area.js 移植):rect 保守覆盖 / circle·poly 格心在内,锚点兜底,双精度同 IEEE 序列。
- **认知变体导航**(`Fog/CrowdBeliefNavs.cs`,对照 beliefNav.js):变体 nav 号 = 槽号 × 65536 + 真 nav 号;belief 字段 = 结构仓形状的认知快照(`CrowdStructuresStore.BuildBeliefField`:**地形源起底 + believed 组件重标**——不能从真相活栅格起底,单侧已知实体的痕迹要靠重标擦掉);变体 = 全拷贝克隆(NavContext 逐格数组全拷、tile 条目经 NavTileCache 内容键共享、HPA 外层容器拷内层共享,裁定 2026-10-09:行为逐位优先,COW 缓行,真机实测后再评)只对分歧矩形(单侧实体 + 未探索 tile 行段)增量重烘;原位揭示(Reveal)只重烘被揭 tile,真变 tile 集与参考端 nav.changedTiles 同口径(NavContext.ChangedTiles);乐观层 fieldFor:未探索 tile 上未被实体覆写的格取 assumedArea(slopeFree 优先、代价最小);sharesFlow 判定移植(不是纯优化——settled 组保不保留旧流场影响刷新计数,是对拍面);detach/settle 全拷贝下天然成立(真相重烘写不到变体自有数组),显式免操作记档。
- **认知同步**(`Fog/CrowdBeliefSync.cs`,对照 beliefSync.js):每 tick 运动后、tick 计数前;槽 0 = 真相(认知集 == 真值集),同认知集的组共享槽(key 桶预筛 + 集合相等确认);BV-6 计数器跳过、探索按 fog.revealTicks 批提交、原位揭示不开新槽;槽切换 → 组换规划句柄 + 领队重挂(F-3)+ 闲置槽休眠、超容量逐旧淘汰(F-4/BV-7)。**双句柄**:Group.NavId = 当前规划句柄(真相号或变体号,槽切换重挂;生成与重下令时挂当前槽),Group.BodyNavId = 真相句柄不变——**意图/领队/规划读 NavId,马达物理/挤离读 BodyNavId**(规划信认知、碰撞信真相)。路径服务的变体号经会话解析器(`ResolveNavContext`),worker 在导航互斥域内解析(Monitor 同线程可重入);认知注册表全部变更经同一互斥域串行。
- **结构仓补桥(F02 对齐)**:地图桥实体以实体身份进认知仓(`InstallMapBridges`:span 即导出端从参考仓读出的 path 足迹,不进地面栅格/碰撞——桥面层仍由 UpperLayerBake 独立烘焙,S1–S7 地面口径零接触);导出器地图实体的写出序改为参考 id 序(不排序),两端实体 id 逐位同构——认知的逐 id 语义(残影/遗忘/共享)以此为前提。
- **修复的潜伏缺陷(迷雾暴露)**:① 结构仓 RemoveEntity 从不清 _ids(死 id 留表);② CrowdIssueOrder 重组用玩家号当表序下标(SpawnAt 传的是 id-1);③ 规划器打单位指令标扫指令簿,回收组号的陈旧链接会命中(改为组的 OrderId 字段,同参考端 g.order.id);④ S1 测试镜像了导出器旧的实体排序(两端同步去排序)。
- **对拍(导出器 S7-b 块 + `S7FogTruthTests`,360 tick × 120 单位,种子 1337,迷雾开启口径——不置 __S7_TRUTH_NAV__,与 S7 冻结块并存)**:脚本 = 部署 → 行军 → 未知建筑(t5 放置初始视盘外,t24 发现,发现前后认知槽 1→0)→ 拆除残影(t90 slot2,t96 再见遗忘)→ reveal/obscure/forget → fogShare 并组(t230)→ fogTerrain 开关对照(t260/t330)。**迷雾 digest 逐位硬门**(s7fog.bin 每组每 tick visible/explored/belief/tiles 计数、beliefKey、槽号、entRev/rev + 全局槽账 seq/switches/entry/dormant/beliefCount——整数集合态无带宽;认知变体的内容一致性由槽字段唯一决定 + 结构 op 报告逐字段 + 轨迹背书)。实测:digest 全程逐位、ops 2 笔全字段、轨迹 p50=0.08/p99=0.45cm;到达判定骑线(L18 族)使末段停车晚 1–9 tick——状态翻转 99 处全为 Moving↔Arrived 到达对(非到达翻转零容忍),末态带 5000(实测 3491);contacts 对齐门 0.5% + ±3(实测 0.147%,直方 [±1:57,±2:3,±3:3],到达时多邻骑线)。**truth 冻结口径**:会话属性 TruthNavFrozen(同参考端 __S7_TRUTH_NAV__ 导出补丁语义)——S5/S7 既有测试置位,旧真值原样全等(已验逐字节);S4 纯部署会话不建迷雾。
- **真机**:S4Deploy 演示脚本追加迷雾命令(reveal/obscure/forget/fogTerrain 对照,12000 单位按认知改道,autoReplay 回放对称);玩家 1 视野组的可见、探索、残影计数在迷雾刷新时维护,调试展开的 HUD 按下标读取;会话累计(重烘、迷雾刷新、等路、规划)在 `CrowdSimulationTelemetry`。乐观开关仍是迷雾状态本身。FogViewGroup 口径(-1 = 全知 debug 视图默认,键切玩家视角,S7 spec 两观察范围并存)与 View(g) 查询面交给迷雾渲染底座(W2)绘制——本单不做渲染。
- **范围边界记档**:LOS 开启(fogSight)的 sight 采样为 C# 双精度域,不是对拍口径(真值场景 LOS 关);sharesFlow 的跨槽流场共享统计(beliefFlowShares)不移植(判定已移植,统计无对拍面);认知槽的 sleep 在全拷贝变体下为显式免操作(容量由淘汰保证)。
## 当前缺口（诚实清单）

- S0 演示 Mod 的屏幕化验收（F1–F8 错误展示、配置清单屏）需要宿主系统接线。
- 多地图共用一份 CrowdSimulationConfig 的寻址方式未定——DeepObject 合并只产出一份配置，按 mapId 绑定意味着一张地图一个地图 Mod；14.1 协议冻结时需定案。
- 桥实体的桥面层导航语义（RT-16 分层）在 S2 接入，S1 只有数据与呈现。
- Raylib 宿主内的地形类型着色 / 阻挡格叠加层还没有 presenter（当前宿主画面是高程着色的高度图地形）；阻挡物还没有呈现网格。
- Web 侧对拍基线的定点数学适配未开工（Fix64 语义在 Ludots 侧，Web 需跟进同一套）。
