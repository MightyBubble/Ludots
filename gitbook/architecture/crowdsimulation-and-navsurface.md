# CrowdSimulation 与 NavSurface：导航体系重构

> 状态：S0（配置门禁）、S1（地形与障碍物）、S2（逐体型可走区域与跳跃链接）、S3-a（tile 烘焙链与 tile 缓存）、S3-b（全局拼装 + HPA* 抽象图,拓扑逐位对拍）、S3-c（走廊查询 + 流场 + 固定生效帧路径服务）、S4-a（ECS 单位部署 + 规范校验码 + 指令记录回放内核）、S4-b（部署演示纯数据化：单位经 presenter 管线进画面,自动部署 + 自动回放对拍）、S5-a（移动内核与阵型：虚拟领队 / 意图 / 马达 / 槽位 / 规划器,轨迹对拍）已交付。S5-b（拖线摆阵演示与走廊扩展）与 S6（避让推挤）见文末路线。

## 这是什么

CrowdSimulation 是新的确定性群体导航内核：分层地表（NavSurface）、逐（移动类型 × 净空）的导航上下文、HPA* 走廊 + 流场、虚拟领队与阵型槽位、局部避让与推挤、脏 tile 增量重烘焙、战争迷雾与认知导航。行为参照是 CrowdSimulation Web 沙盒（Base44 参考实现，16 km 地图、5 万单位上限），交付节奏按该项目的 S0–S8 阶段与 UAT（F-01–F-13）执行。

NavSurface（`.navsurface`）是新增的地表资产：逐导航格的地形类型编号 + 跳跃候选。它是导航的唯一地表输入；高度真相仍在 `.height`，桥面不写进地表——桥实体在运行时生成独立的桥面层，拆桥即恢复。

## 为什么不用 MassNavigation 求解器

四个结构性原因（证据见 Web 规格 LU-12 ~ LU-15）：每个成员同步寻路且每次调用都重建网格；地图变化不会让路线失效；方向场是启发式而非沿路网积分；避让与到达规则参数无法一一对应。调参数对齐不了行为，所以内核按 CrowdSimulation 重写。

并存期的纪律：**一张地图只能启用一种求解器**（加载时按 mapId 互斥检查），同一单位模板不能同时带 `CrowdSimulationAgent` 与 `MassNavigationAgent`。MassNavigation 的删除不做在对拍通过之前——它仍被 GAS 下令链、Physics2D 桥、10k 性能门禁与 showcase 引用，对拍基线建立后按 S8 之后的决议退役。

## 数学一律走 Ludots 定点体系

不引入第二套数学：内核全部使用 `src/Core/Math/FixedPoint` 的 Fix64 / Fix64Math（Q31.32 与其确定性超越函数），长度单位为厘米，与 `WorldPositionCm` 同单位制。配置文件维持 Ludots 约定（厘米、`Cm` 后缀），文件值在组装层一次性转 Fix64，内核不再做单位换算。Web 侧的 `DetMath` 计划由此废止——对拍时以 Ludots 定点语义为准生成基线，Web 参考实现如需对拍要适配同一套定点算法。

烘焙期边界：运行期内核（部署 / 移动 / 规划）全 Fix64；`NavHeightField` 的 .height 样本解码与 `NavTileBaker` 的简化 / 边长阈值比较、格心归属判定允许 IEEE double，产物进定点域（定点厘米 / 整数格角点）前一次性转换，依据见 S2 段——定点域内直接乘会溢出 Q31.32，且 Web 导出端同样从量化样本重建，两端读同一份输入。

## 目录落点

```
src/Core/CrowdSimulation/            内核（命名空间 Ludots.Core.CrowdSimulation.*）
  Config/                            CrowdSimulationConfig（DTO，严格 camelCase 全必填）
                                     CrowdSimulationConfigValidator（数值 + 结构规则）
                                     CrowdSimulationConfigLoader（LU-20 组装入口）
                                     CrowdSimulationAuthoringContract（模板级契约）
  World/                             NavSurfaceAsset（.navsurface v1 读写与格式门禁）
                                     NavSurfaceContract（资产 ↔ 配置一致性）
                                     CrowdSimulationSpace（格几何原语）
                                     SurfaceGrid（地表栅格:地形 / 区域 / 阻挡）
                                     NavHeightField（.height → 导航高度场与坡度）
                                     CrowdSimulationMapSurfaceSource（地图实体提取）
  Nav/                               NavContextBaker（分类 / 腐蚀 / 连通域 / 链接）
                                     UpperLayerBake（桥面层栅格化与烘焙、portal 合并）
                                     NavContext / NavLinkSet（上下文产物）
  CrowdSimulationAgent.cs            单位组件 { profileId }
mods/capabilities/navigation/CrowdSimulationMod/
  assets/config_catalog.json         登记 CrowdSimulationConfig.json = DeepObject
  assets/CrowdSimulationConfig.json  全量默认值（地图 Mod 只写要改的字段）
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

内核里有四组与主干既有设施功能平行的组件，每组对应参考实现（CrowdSimulation Web 沙盒）的一个文件。它们是逐 op 对拍的移植面，替换即破对拍，处置原则是保留并写明对应物：

- `Nav/NavMinHeap` ↔ 主干 `Collections/Fix64PriorityQueue<T>`。参考对应物 `engine/heap.js`。移植保持同构：穴插法上滤 / 下滤、同级并列取左孩子、Fix64 键 + int 值并行数组、零逐节点分配。S3-b 抽象图拓扑逐位 FNV 对拍挂在它的弹出序上；换泛型堆需要重新证明同键并列的弹出序一致，对拍表面不动。
- `Units/CrowdSimRng` ↔ 主干 `Engine/Randomization/RngStreamService`。参考对应物 `engine/noise.js` 的 mulberry32。两者算法不同（RngStream 是 xorshift32，CrowdSimRng 是 mulberry32）、输出转换不同（RngStream 的 NextFloat01 是 24 位整数 ÷ (2²⁴−1)，CrowdSimRng 是 u32 ÷ 2³² 直进 Fix64 原始值——二进分数，精确）；盐流合同也不同：部署按指令加盐（`CrowdDeployment` 用 worldSeed × spawnSeq 组合派生每条指令自己的流），RngStreamService 是先声明后取用的命名流注册表。S4 部署对拍要求 12020 个单位位置原始值逐位一致，换流必破。
- `Units/CrowdSimCommands` + `CrowdCommandQueue` ↔ 主干 `Persistence/ReplayRecorder`。参考对应物 `engine/core/commands.js` 的 CommandQueue。指令队列是内核输入面：tick 边界执行、submit 现场记日志、schedule 与存量按 tick 归并、迟到指令按实际执行 tick 记录；日志 = 完整输入，回放 = 新会话 + 同一日志。它记录的是仿真输入指令，不是引擎权威帧，两者不是同一类东西的两种实现。
- `Nav/NavFunnel` ↔ 主干 `Navigation/NavMesh/Bake/FunnelAlgorithm`。参考对应物 `engine/navquery.js` 的漏斗。判等语义是硬分界：NavFunnel 全 Fix64 精确等（`ax == rx`，与参考实现的 JS 数值严格等一致），主干版是 float32 Vector2 + 1e-6 容差近似等；漏斗折线是领队路径的直接输入，退化 portal 处顶点差一位，后续 600 tick 轨迹全变。

四件均非历史遗留（各有活跃消费者与对拍义务），无收编项。

### 指令日志能否由 ReplayRecorder 承载：不能

边缘接入评估的结论：记录面留在 `CrowdCommandQueue`，内核语义不变。阻塞理由：

1. 帧载荷装不下。`AuthoritativeAction` =（ActionId 字符串，Vector3 float32，三个布尔），帧内 ActionId 按字典序唯一、浮点必须有限；JSON 指令的字符串字段（type / shape）、嵌套数组（face[2]）、同 tick 内的提交顺序都没有无损落点。任何编码器都是新的有损适配层，压在逐位对拍的输入上。
2. 检查点生产者不存在。`ReplayRecorder.SetCheckpoint` 要求 `WorldSaveSnapshot`（engine.World 二进制 + ModSetHash / RegistryFingerprint 头），只能由 `WorldSnapshotService.Capture(GameEngine, boundary)` 产出；对拍与回放会话是一次性 `ArchWorld.Create()`（无 GameEngine，无 save participant 注册表）。
3. 播放端同样要 `GameEngine` + `WorldRestoreService`（`ReplayPlayer.PlayFromCheckpoint`）；crowd 回放契约"新会话 + 同一日志"已由 `CrowdSimulationRuntime.RunReplay` 与 `S4DeployTruthTests`（同一记录回放两次校验码相同）覆盖，换播放端不增加能力，只增加依赖。

重开条件：指令载荷进入引擎权威帧合同（字符串 / 嵌套 / 顺序可无损表达），且 crowd 会话具备检查点的生产与消费管线。

## 配置门禁（S0 的核心交付）

加载在进入地图时进行，错误消息写明文件、字段路径与规则，例如：

```
CrowdSimulationConfig.json: hpa.clusterSize = 2，需为整数、≥ 4、≤ 128
```

覆盖的规则：声明式数值范围（与参考实现 schema.js 的 RULES 同源，路径已按 Ludots 文件归属换算）、id 唯一性与容量上限（区域 ≤ 256、移动类型 / 单位模板 ≤ 255、组合 ≤ 255）、引用闭包（areaCost / profile / agentType / deploy.bases.playerId / CrowdSimulationAgent.profileId）、地图正方形与格数整除、1–16 个玩家、双求解器互斥、阻挡物只能是正方形 Box（不禁止圆形非阻挡区域实体）。版本不等于当前 v7 一律拒绝，无迁移。例外：formation.headingInheritDot / formation.mirrorFlipDot 是 Ludots 侧的配置化补充——参考实现里是 planner.js 的内联字面量（0.94 / -0.5），默认值与字面量同值，规则是点积阈值域 [-1, 1] 的两半（mirrorFlipDot ≤ 0 ≤ headingInheritDot）。

## .navsurface v1 格式要点

小端二进制：`magic "LNSF"` + version + 尺寸 / 格边长 + 地形类型表（id 字符串，格值 = 表下标）+ 逐格 u8 地形编号 + 跳跃候选（端点格、落差厘米、格距）。读取侧做精确长度校验（多一字节少一字节都拒绝）、编号越界拒绝、端点越界拒绝。资产必须与配置的 `world.terrainTypes` 逐字同序——编号语义是“表中下标”，顺序错一位全图错位。

## 阶段路线（来自 Web 规格第三部分）

S0 配置门禁 → S1 地形与障碍物（读 .navsurface、阻挡实体生成障碍）→ S2 逐移动类型可走区域与烘焙 → S3 后台路线与流场 → S4 部署与命令回放 → S5 移动与阵型 → S6 避让推挤 → S7 动态地图与迷雾认知 → S8 正式下令链与 5 万单位。上一阶段对拍通过后才进入下一阶段。

## S1 交付：地形与障碍物（本次）

- **导出工具**：`tools/crowdsimulation-export/exportLudots.mjs`（正本在本仓库，运行于 Web 沙盒 `scripts/` 下），把作者态测试场景（AU-07 / AU-08）按种子写成 Ludots 资产——`.height`（CHTM v2，uint16 缩放）、`.navsurface`（v1）、`Maps/<id>.json`（队伍 / 敌对关系 / 300 阻挡物 / 桥实体）、配置覆盖片段与 S1 对拍摘要。坐标约定：Web 的米 × 100 = Ludots 地图局部厘米，轴向沿用同一平面朝向。
- **演示 Mod**：`mods/showcases/crowd_simulation/CrowdSimulationS1Terrain{1337,2024,7}Mod`，三个种子各一个 Mod，launcher 选择即切换（config 按 mapId 绑定地图，多地图共用一份配置的问题见缺口）。
- **验收（自动化，`src/Tests/CrowdSimulationTests/Parity/`）**：三种子的地形类型栅格、区域栅格、阻挡栅格、阻挡物 / 桥 / 跳跃候选清单与 Web 导出做 FNV-1a 逐格对拍，全相等；删一个阻挡物栅格变、改模板尺寸全体变、覆盖率阈值（< 0.5 不阻挡）生效；`.height` 经 Ludots 正式读取器加载。
- **验收（目视）**：`artifacts/acceptance/crowdsimulation-s1/index.html` 查看器，`CrowdSimulationMapProbe` 工具从真实资产渲染地形类型着色 / 阻挡格 / 桥与跳跃候选三层视图。

## S2 交付：每种单位能去哪里（本次）

- **导航烘焙**（`src/Core/CrowdSimulation/Nav/`，参考实现 `nav.js` / `navLinks.js` / `navtile/upperLayer.js` / `erode.js` 的 S2 子集移植）：逐（移动类型 × 净空）导航上下文 = 代价栅格（区域代价行 × 坡度上限 × 阻挡）→ 切比雪夫净空腐蚀 → 可走栅格 → 4 连通洪泛连通域 → 跳跃链接（候选按 profile 的 up / down / rangeCells 过滤成单向 / 双向，携带连通域间有向可达图）。
- **桥面层**（RT-16 的网格级部分）：桥实体栅格化为桥面格 + 桥头 portal;桥面可走格 = 区域定价 ∩ 净空腐蚀,桥头腐蚀半径内的地面可走格并入;连通域合并 = 桥面洪泛 + portal 格双通合并（与参考实现 mergeTiles 同语义）。
- **高度输入**：`NavHeightField` 从 `.height` 资产的 uint16 样本推导导航格高度与坡度;样本解码先在 double 完成（raw × 分子 ÷ 分母）再转 Fix64——定点域内直接乘会溢出 Q31.32。Web 导出端同样从量化样本重建,两端读同一份输入。
- **对拍**：导出器跑参考实现的真实 `buildNavContext` 产出逐上下文摘要;C# 侧重建同序字节流,FNV-1a 全等（5 上下文 × 3 种子）,连通域按划分规范化比较（不按标签编号）。`JumpLinkFilter_RespectsDownCm` 覆盖跳跃能力收紧后链接减少的验收点。
- **目视**：探针 `--s2` 模式渲染逐体型可走区域（不可走遮罩 + 桥面 + portal + 链接）,同一查看器切换。
- **已知边界**：坡度阈值处的定点 vs 浮点残差实测约 1e-5,三种子均未翻转任何格。

## S3-a 交付：tile 烘焙链与 tile 缓存（本次）

- **移植**（`Nav/Recast/` 与 `Nav/`,整数格角点几何,无浮点参与）：距离场（`DistanceField`）、分水岭区域（`WatershedRegions`,含计数排序 / 层级生长 / 小区域合并的完整顺序语义）、轮廓追踪（`ContourTracer`,tile 边界永不简化为墙、格角必留、Douglas-Peucker 整数叉积 + 坐标决胜）、耳切三角化与洞合并与凸合并（`PolygonOps`）、`NavTileBaker`（tile → 凸多边形 + 邻接 + 边界段 + cell→poly）、`NavTileCache`（内容键 LRU,无哈希碰撞）。
- **桥面 tile 与地面 tile 共用同一缓存**,条目是独立内容键;无可走格的类型（如 hull 的桥面）不产生条目——S1 先修了一个 +1 编码双重叠加导致的键错位。
- **对拍**：导出器把五种上下文共享的 tile 缓存逐条目（内容键指纹对齐）落真相,C# 逐字段 FNV 全等——三种子各 769 / 700 / 883 个不同 tile,缓存命中 / 烘焙计数也一致。
- **踩过的坑（已固化成回归测试 `S3RegressionTests`）**：定点 `Sqrt` 在"路径端点格"上的 ~1e-5 误差会吃掉覆盖判断的 EPS——桥 / 路径跨度按生成器约定轴对齐,轴对齐时单位向量与长度取精确值;斜线路径（S7 道路 / 泛洪）届时再定精度口径。
- **目视**：探针 `--s3` 渲染 NavMesh 多边形网格叠加图（`crowd_simulation_s1337_navmesh_foot.png`）。
- **S3 剩余**：走廊（A* + 漏斗）、流场 Dijkstra、路径服务固定生效帧——S3-c 已交付,见下。

## S3-b 交付：全局拼装与 HPA* 抽象图（本次）

- **移植**：`NavMeshAssembler`（跨 tile 多边形拼接 + 边界重叠段缝合为 portal,重叠对按不可变条目对记忆化）、`HpaGraph`（cluster = tile 的入口扫描、簇内 Dijkstra、逐 cluster 不可变块、跳跃链接作有向抽象边、桥面节点键 = N² + 格且只在 portal 跨层 0 代价相连）、`NavGridSteps` / `NavMinHeap`。
- **对拍**：拓扑(节点格 / 邻接目标 / 边数 / 网格多边形结构)逐位 FNV 全等;代价数组按带宽比较,三种子实测最大分歧 ~2e-5(Fix64 与 f32/f64 两条算路的累积差,写进测试断言与文档)。
- **审计**：每阶段交付后跑交叉对抗审计。本轮 A 链(导航烘焙)12 片 + B 链(S0–S2)6 片全绿,唯一实质发现是 tile 缓存少了参考实现的 bakeMs 遥测(已补)与单线程归属契约没写清(已写)。
- **目视**：探针 `--s3` 视图叠加了 cluster 网格与入口节点。
- **验收（真机）**：Raylib 宿主经 launcher 预设启动，地图、阻挡物实体与高度图地形（含高程着色）真实渲染，见 `artifacts/acceptance/crowdsimulation-s1/raylib-s1337-live.png`。地形类型着色叠加层在宿主内尚无 presenter，由探针视图承担。
- **顺手的基建修复**：`ContinuousHeightSampleScale.Decode` 的 int32 溢出（大缩放比例资产会触发，10k 资产因恒等比例从未踩到）改为先 widening 再乘。

## S3-c 交付：两点之间的路线（本次）

- **移植**：`TilePathQuery`（tile 寻址的多边形 A* + 跨 tile 缝合 + 桥头 portal 跨层）、`NavFunnel`（简单愚蠢漏斗）、`HpaQuery`（抽象图 A* + 簇内 Dijkstra 起收尾）、`FlowField` / `FlowPool` / `FlowFieldBuilder`（积分场 Dijkstra 限走廊掩码 + 按弹出序拉直 + 路点链）、`CorridorQuery`（corridorTo：无链接先 NavMesh,有链接直走 HPA*,失败回落;桥面起点 BFS 下桥）、`PathQueryService`（固定生效帧路径服务）。
- **对拍契约（两套浮点体系下唯一诚实的口径）**：逐位 FNV 只对真整数——分支选择（NavMesh / HPA* / 不可达）与 trace 是否到达;代价类（integ / 路径代价）紧带宽 5e-6;几何类（折线总长 / len）松带宽——等代价的十字路口择路被最后位差翻转时两解同最优,几何却不同;到达集合 / 路点是浮点派生量,按"独占格必须在走廊分歧的 cluster 内或交集洪泛外"与"决定性全等、平局双解皆合法"规则校验。实测三种子 64 对 × 5 上下文:代价最大分歧 2.6e-6,约 497 万到达格路点全等、1475 个平局格全部按规则通过,独占格（三种子合计 1626 个）全部是整 cluster 的走廊择路分歧。
- **服务验收（`S3PathServiceTests` / `S3SoakTests`）**：后台线程 1 / 2 / 4 的 320 份答复滚动 FNV-1a 64 逐字节等价（每 worker 独占搜索缓冲,RT-03）;生效帧恒为请求帧 + latencyTicks,后台慢注入时仿真原地等待、答复内容与帧序不变;单次规划超 planTimeoutMs 即故障报错,不重试不换算法;浸泡 10 仿真分钟（18000 帧 × 3000 请求）池 allocated 预热后恒为 2、托管内存平在 26 MB。
- **踩过的坑（已固化成测试或注释）**：链接端未到达时 `integ + cost` 在 Fix64 下溢出回绕成负数、判等即真（参考实现靠 Infinity 语义天然免疫）,加未到达守卫;stitch 记忆化按 uid 做键,跨缓存实例撞号（三个种子连跑才暴露）,改条目引用键;tile 多边形代价备忘挂在 worker 暂存上,跨上下文复用时串味,键补 navId;流场回收全进 0 号 worker 的池,另一池永远得不到还回、无法平台化,改按 OriginPool 物归原主;worker 崩溃曾经悬挂所有在途请求,改为服务故障（报错,绝不静默）。
- **演示**：探针 `--s3c` 渲染折线 + 方向箭头（`artifacts/acceptance/crowdsimulation-s1/crowd_simulation_s1337_s3c_*`）;`CrowdSimulationS3PathMod` 真机演示（`raylib-s1337-s3path-interactive.png`）按 spec 演示契约全部接上：**左键点起点、右键点终点**（光标走 ScreenRayProvider + 高度图射线落地）,**Q 巡回代理体型**（全部去重导航上下文）,**T 巡回后台线程 1/2/4**（结果与线程数无关）,**G 注入后台变慢**（仿真暂停等待,画面不卡,绝不使用半成品）;HUD 实时显示请求帧 / 生效帧 / 当前帧 / 等待次数 / 体型 / 线程数 / 分支 / 可达性。呈现全部走正式基建,不吃调试通道：方向图经新增的**全场投影器注册表**（`GlobalFieldVisualProjectorRegistry`,宿主不再逐个认识场）写进全场视觉缓冲,再由**地形贴花槽**渲染（复用旧 walkability texture 通道:`SetNavWalkabilityOverlayExternal` 外部持有纹理 + Flow 场契约 Vector4 = 方向 + 强度,方向取色相、强度取不透明度,双线性过滤,整张贴花随地形,不再有逐格小方块）;折线与起终标记走新增的**路线视觉缓冲**（`RouteVisualBuffer` + `RaylibRouteVisualRenderer`,gameplay 通道,地形跟随,移动路线 / 阵型槽位后续同路）。调试绘制顺带修了地形跟随（`GroundSamplerM`）。
- **顺手填掉的基建缺口**：宿主场投影块的影响场分支原来错嵌在"离散权属存在"的条件里（本图没有离散权属就不执行,嵌套从合并遗留,这次才暴露）——离散 / 影响 / 注册表三路已拉平到同一层;URI 版 `ClearNavWalkabilityOverlay` 原来每帧无条件清槽,会把外部持有的贴花一起清掉（贴花接线上轮怎么都画不出来,根因就是这个清绑交错）——清除按纹理所有权分流,URI 配置轮询只管自有绑定;overview 简化网格原来一律洗掉 walkability 叠加,Flow 类贴花加了 `NavWalkabilityOverlayVisibleInOverview` 按场类保留;贴花 drape 的逐格小方块路径保留为无连续高度图时的兜底。
- **顺手的输入接线**：点选输入走引擎现成的 `InputBackend`（鼠标 / 键位）+ `ScreenRayProvider`（平滑渲染相机射线）+ 高度图 `TryRaycastGround`（落点必须逐帧取服务——GameStart 时高度图还没注册,一次捕获就是 null,点击全丢）,呈现线程只入队、仿真 tick 消费;被取代的请求由新增的 `PathQueryService.Discard` 作废（未开工不算、已算完丢弃）。分层视图:V 巡回 路线(方向场贴花) / 可走区域(新增 `GlobalFieldVisualKind.Walkable` 直接 RGBA 场类,不可走红罩 / 桥面棕 / portal 黄) / NavMesh+HPA 线框(多边形描边 + cluster 网格 + 入口节点 + 跳跃链接,路线通道共享数组缓存)。

## S4-a 交付：部署单位与指令回放内核（本次）

- **单位 = Arch ECS 实体**（不克隆参考实现的 SoA 存储）：`CrowdSimulationAgent`（profileId）、`CrowdSimulationUnitState`（槽位 / 组 / state 等校验字段）、`WorldPositionCm`、`PlayerOwner`（Ludots 玩家号 1..P）。管理层 `CrowdSimUnits` 只持稠密序 + 18 位槽位 + 14 位代的句柄池（与参考实现 units 的句柄同构,回放可复现）;`CrowdNavGroupSet` 是（玩家 × 移动类型 × 半径级）的组注册表。
- **部署算法逐 op 移植**（sim/population.js 的 spawn / spawnAt）：mulberry32 加盐流（每条指令自己的流,输出 u32/2³² 二进分数,Fix64 精确表示）、出生点抖动 → nearestPassable 环形搜索 → 编组中心 → 三角散布 → 格内 inset 落点。满容量（`sim.maxUnits`）拒收并计数（D54:失败不留半生成状态）。
- **规范校验码（FNV-1a 32,Fix64 原始值口径——甲方体系）**：字段顺序与参考实现 unitChecksum 一致,定点字段按原始 int64 低→高 32 位混合。位置在两端同网格：生成公式在 Ludots 是 Fix64 算术,参考端经镜像侧 S4 归一补丁按同一语义逐 op 求值（FromDouble 向零截断、乘法向 -∞ 取整;**位置乘法在两端都精确,不允许再取整**——米域取整比厘米域少 50 个原始单位,已踩过并固化）。
- **指令队列与回放**（core/commands.js CommandQueue 移植）：指令是数据,tick 边界执行;日志 = 完整输入,回放 = 新会话 + 同一日志。会话的 tick 就是 `Engine/clock.json` 的 FixedHz,不存在第二个频率。
- **对拍（`S4DeployTruthTests`,3 种子）**：同一份脚本（spawn 12000 → 框选 → 点名生成 20 → 全选 → 清空）逐帧校验码与沙盒一致（150 帧 × 3）;12020 个单位逐条（玩家 / 模板 / 半径级 / 组 / 位置原始值）一致;同一份记录回放两次校验码相同。
- **踩过的坑（已固化）**：S0 配置的 `deploy.bases` 玩家 4 出生点 y 写成了玩家 2 的值（0.65 → 0.7 份额,部署差 416 格才发现;组号全对上但中心全错,按"组-中心-RNG 值"三层对拍才定位）;镜像侧量化函数把位置乘法也取整,奇数半档比 Ludots 少 50 个原始单位;参考实现玩家 0 基与 Ludots 玩家号 1..P 的换算只在 `PlayerOwner` 与脚本数据边界做一次,内部编号序不受影响。
- **配置增量**：`world.seed`（生成盐,进覆盖配置）、`unitTypes[].special`（特殊单位不参与批量部署）。

## S4-b 交付：部署演示纯数据化——单位进 presenter 管线（本次）

- **会话宿主下沉 Core**：`CrowdSimulationRuntime`（`src/Core/CrowdSimulation/Runtime/`,MassNavigationRuntime 同款形态）在地图聚焦时按配置目录合并出的 CrowdSimulationConfig + CrowdSimulationDebug 建会话——会话跑在**引擎世界**上（此前演示 mod 在侧世界 `ArchWorld.Create()` 起会话,呈现管线根本看不见单位,这是 S4 演示最初看不到单位的根因）,tick 由系统组的 FixedHz 驱动,会话与运行时注册成服务。S1/S3 演示 mod 的同款 Load 样板后续都往这里收。
- **单位接入呈现投影（不造第二条路）**：`CrowdSimUnits.Add` 在接线存在时给单位挂齐呈现预置件——`PresentationStableId` + `EntityTemplateKeyRef`（模板键来自 `agent_profiles.json` 新增的 `templateId`,按体型族映射）+ `PreviousWorldPositionCm` + `VisualTransform` + `CullState` + `ContinuousHeightmapSampleState` + 半径黑板。之后 `PresentationEntityLifecycleSystem` 按 presenters.json 规则数据驱动地建/毁 presenter,与生成队列出生的实体同一条路。无头对拍与回放传 null 接线,组件一个不挂,71/71 逐位不变。
- **呈现全部声明式**：形状 = 体型模板键（步兵=方/水军=圆/山地=三角/两栖=菱形(立方体 localRotation 45°)/跳跃=十字(body+orientation 双槽位十字梁),前二用内置原语,三角形是新增的 `unit_triangle.gltf` 数据资产,走 mesh_assets + host_assets 的正式宿主绑定）;玩家色 = presenter `entityColorVector` 绑定 + 新增的 `TeamColorPalette` 服务（`deploy.bases[].color` 是配置数据;`PresenterBehaviorSystem` 优先读调色板,未注册回退原双色解析,旧场景零影响）;选中环 = select 指令执行时镜像进实体集合仓的 `selected` 集合,presenter 监听 `EntityCollectionMemberAdded/Removed` 建/毁选中环（GroundOverlay Ring,`scaleParamKey` 绑单位半径黑板随体型缩放）。校验码不含选中,镜像不进对拍口径。
- **演示 = 数据脚本**：S4 演示 mod 删成纯数据（无 main）——`CrowdSimulationDebug.json` 声明 `session.autostart` + 一段与对拍资产同构的脚本（spawn 12000 → 四个基地外,另在地图中心给四个玩家各点一簇 300 单位的展示生成,让镜头够得着）+ `autoReplayAtTick:150` 自动回放。真机日志：13220 单位全数落地、20 个体型 presenter 定义各数百实例激活、首单位视觉变换贴在地表（446.7 m）、回放逐帧对拍一致（status=1）。
- **踩过的坑（已固化成代码注释）**：① 缺 `PreviousWorldPositionCm` 时 `WorldToVisualSyncSystem` 的查询直接捞不到单位,`VisualTransform` 永远停在默认值,presenter 全沉在（0,0,0）;② 缺 `ContinuousHeightmapSampleState` 时地形高度同步不写 Y,单位埋在海平面以下的地表里（探针打印首单位 vt 才看见 Y=0）;③ 引擎全局档案注册表是多特性合并产物,Core 的小体型（r20~80）并进来会把半径级挤歪、`NavFor(1,0)` 直接缺键——crowd 会话的档案来源改由 `agents.profilesUri` 显式声明,默认才回退全局注册表;④ presenter 配置的 `extends` 合并到行为槽位为止,`assetBinding` 是整换不并字段,变体干脆整段写全;⑤ 行为槽位有注册表,`cross` 的两根梁用 `body`+`orientation`,不能自造槽名。
- **相机**：S4 mod 用同 id 片段把演示相机的 `edgePanMarginPx` 关成 0（无头跑证据时指针静置在窗口边缘会把镜头慢慢拖走,抓屏全都对不上）。

## S5-a 交付：移动内核与阵型（本次）

- **移植**（`src/Core/CrowdSimulation/Movement/`,对照参考实现 `movement/{leaders,intent,motor,flowSample,contact,walls}.js`、`orders/order.js`、`formation.js`、`planning/planner.js`、`sim/issueOrder.js`）：`CrowdLeader`（虚拟领队：前视转向 / 限速 / D61 镜像 / D62 朝向继承 / SetStart）、`CrowdIntents`（槽位 ↔ 流场 blend、LOS 滞回、D59、车道展开、D60、停滞触碰到达、LY-5 桥头层切换）、`CrowdMotor`（一阶响应 / 转向限速 / 速度上限 / 墙面滑动 / 跳跃）、`CrowdFormations`（assignSlots / relativeSlots / layoutSideBySide）、`CrowdOrderBook` + `CrowdIssueOrder`（magic box chooseMode、D57 去重、重组）、`CrowdSimPlanner`（plan / applyDue / finishOrder,走 S3-c 的固定生效帧路径服务）、`CrowdSpatialHash` / `CrowdWalls` / `CrowdContact` / `CrowdBlockerColliders` / `CrowdFlowSample`。
- **会话接线**：`CrowdSimSession.Step()` 返回 `string?`——规划答复未回时返 null,引擎让出本帧（`BlockOnDueReplies` 区分真机让出与无头同步等待）;`CrowdSimulationKinematics` 恒挂;校验码运动字段从占位零改读真值;`NavContext` 收编 `CanReach`（LY-4：上层编号接地面顺排 + 桥头互通边）,`NavLinkSet` 退回纯数据。`order` 指令进 `CrowdSimCommands.Exec`。
- **对拍（`S5TrajectoryTruthTests`,600 tick × 241 单位,种子 1337,crowd 全量 75/75）**：状态机字段（state / mode / level / order）全程逐位全等;位置是浮点派生量,按逐 tick 带宽比对——实测 p50 = 0.06 cm、p99 = 2234 cm、max = 6909 cm,带值定案 `MaxTrajectoryBandCm = 10000`、末 30 tick `FinalBandCm = 500`（实测 375.6 cm 且递减,是再收束不是发散）。两端挂同一屏蔽开关 `__S5_NO_AVOIDANCE__` 关避让推挤（避让是 S6 的活;哈希照建,到达与接触语义不变）。真值资产 `s5-trajectory.bin` + 脚本 JSON 进 S1Terrain1337Mod 的 parity 目录。
- **踩过的坑（已固化成代码注释或测试）**：① Fix64（Q32.32）的平方在厘米域约 463 m 就溢出——领队 aim / step、拉直路点方向、槽位距离、chooseMode spread 全部中招（山地领队曾因此瞬间走完全路径、路点方向曾全零致单位卡死）,`CrowdFix.Hypot` 先按最大值归一再开方,厘米域长度一律走它;② 碰撞半径口径是**个人半径**（radiusCm × avoidanceRadiusScale = 600 cm）,不是导航半径 100 cm——墙面滑出 / 接触 / 阵型占地 / 空间哈希环统一读 `ProfilePersonalRadiusCm`（真值里 u173/u174 钉在面 − 600 实锤）;③ 马达静止分支的跳过条件是 `rest && calm && 全零`——S5 关避让后 calm 恒 0,静止单位照过马达,压住被挡格时的墙面滑出是真实位置来源;曾误加成全零即跳,已还原（S6 接 calm 时再评估是否恢复跳过）。
- **真机演示**：S4Deploy 演示相机补 `targetSource: Fixed` + `fixedTargetCm`——此前 `VirtualCameraRequest` 只带相机 id,轨道目标沿用地图默认相机位姿（世界中心、19.2 km 视距）,12000 单位全程在画幅外;Fixed 位姿是 `VirtualCameraBrain` 的现成激活语义,零代码。视距定在 400 m：r400 单位（8 m 方块）约 7 px,阵型间距与玩家色可读,`allowUserInput` 保持,可自由缩放平移跟随行军。验收图 `raylib-s1337-s5move{4,5,6}.png`（1.5 km 行军带 / 800 m 阵型散布 / 400 m 单位特写）。
- **审计**：交叉对抗审计 A 链 8 片（leader / intents / motor / formations / orders / planner 两片 / support）+ B 链 3 片（会话停摆 / 导出契约 / 运行时指令）全绿,托管 deepseek-v4-pro;spark 抽查 s5-intents 空响应（已知不稳,未计入）。两片附注确认审计真实读码：motor 片的 metrics / calm 裁剪与 support 片的定点等价表达均被识别为声明过的移植口径,不计缺陷。
- **S5 剩余（S5-b）**：拖线摆阵演示（走 S3-c 路线视觉缓冲同一条路）、走廊扩展 processPending、多起点 D08 规划;避让推挤归 S6。

## 当前缺口（诚实清单）

- S0 演示 Mod 的屏幕化验收（F1–F8 错误展示、配置清单屏）需要宿主系统接线。
- 多地图共用一份 CrowdSimulationConfig 的寻址方式未定——DeepObject 合并只产出一份配置，按 mapId 绑定意味着一张地图一个地图 Mod；14.1 协议冻结时需定案。
- 桥实体的桥面层导航语义（RT-16 分层）在 S2 接入，S1 只有数据与呈现。
- Raylib 宿主内的地形类型着色 / 阻挡格叠加层还没有 presenter（当前宿主画面是高程着色的高度图地形）；阻挡物还没有呈现网格。
- Web 侧对拍基线的定点数学适配未开工（Fix64 语义在 Ludots 侧，Web 需跟进同一套）。
