# NavMesh 功能目录

这里把导航体系拆为 24 个独立功能：先确定作者要编辑什么，再定义工具如何处理，最后说明 Runtime 如何消费，以及玩家如何在演示中看懂结果。总合同见 [NavMesh 导航体系 SSOT](../navmesh-ssot.md)。

## 功能地图

编号用于定位文档，不代表每项都需要新开 issue。区域与代价沿用 #372：地图存分类，导航按 Agent × area 解析成本；主线矩阵已有，不能列作重新设计的任务。每页末尾按编辑器、工具、Runtime、Showcase 列出自己的剩余工作与验收条件。现状核对基线为 2026-09-08 的 `origin/main 9231f05fcf`；功能页中的目标设计不能当作当前 API。

| 功能 | 单一责任 | 核查状态 | 所属总 TODO |
|---|---|---|---|
| 01. [每板寻址](board-addressing.md) | 板内寻址与产物隔离 | 每板配置已入主线；完整寻址未收口 | 1 |
| 02. [空间尺度与分辨率](spatial-scale-and-resolution.md) | 世界、板、chunk、NavTile、FlowWindow 的尺度 owner | 空间尺度 SSOT 已有；导航编辑器联动待补 | 1/4 |
| 03. [拓扑与逻辑地形](topology-and-logic-terrain.md) | Grid/Hex/NodeGraph 与视觉/逻辑地形边界 | Grid/Hex 适配已入主线；单源投影仍待收口 | 3 |
| 04. [路由域选择与动态边权](route-domain-selection.md) | Graph/Mesh/Auto、projection、GraphEdgeCostOverlay | 路由选择主线已有；动态 overlay 和完整作者面待补 | 3/4 |
| 05. [路线到 MassNavigation 执行](route-execution.md) | 路线、waypoint、MassNavigationFlow 执行边界 | 主要链路已入主线；完整 profile 双路 showcase 待验收 | 3/4 |
| 06. [烘焙估算、校准与内存预算](bake-estimation-and-memory.md) | full/dirty/window 估算、校准和大图内存边界 | 理论模型已有；真实校准记录与统一入口待补 | 2/4/5 |
| 07. [多层寻路](multi-layer-pathing.md) | 通行层/重叠表面选择 | 通行层基础已有；桥上下表面链未收口 | 3 |
| 08. [Agent 半径与 Profile](agent-profiles.md) | 体型、profile 引用和半径匹配 | profile 基础已有；作者配置与实际到达待补 | 4 |
| 09. [坡度、台阶与净高](slope-clearance.md) | 几何阈值与上下表面净空 | 坡度/爬高基础已有；净高源未收口 | 3 |
| 10. [Runtime 脏区重烘焙](runtime-dirty-rebake.md) | 变化影响范围和路径刷新 | 主线已有队列；全队到达待验收 | 2 |
| 11. [Runtime 快照、预算与代次](runtime-bake-budget.md) | 不可变输入、调度与旧结果丢弃 | #1164 分支有 worker/代次；主线未收口 | 2 |
| 12. [结构障碍足迹](structural-obstacles.md) | 建筑/墙/门的导航输入 | provider/dirty system 已有；完整足迹工作流待补 | 3 |
| 13. [投影可行性贴图](walkability-projection.md) | 地表投影图像及来源版本 | PNG/overlay 已有；版本元数据与过期处理待补 | 5 |
| 14. [NavMesh 真实调试显示](navmesh-debug-view.md) | 真实三角形、边界、瓦片拾取 | 真实 Raylib 显示已入主线；交互检查待补 | 5 |
| 15. [NavMesh Link](navmesh-links.md) | 非连续连接与动作交接 | 尚未实现，不可玩 | 3 |
| 16. [烘焙源接入](source-and-semantics.md) | 高度/grid/hex 统一输入 | 命名/高度场直灌已入主线；源合同待收敛 | 3 |
| 17. [区域分类与 Agent 通行代价](area-costs.md) | area × Agent 代价矩阵 | 主线矩阵已有；去 Cost 和纯 NavMesh 接线有未合分支 | 3 |
| 18. [水面导航语义](water-semantics.md) | 水深、吃水和船宽 | 水域阻挡片段已有；水深/吃水链未收口 | 3 |
| 19. [NavTile 产物与 Manifest](artifacts-and-manifest.md) | 持久产物、身份与冷加载 | .ntil/Store 已有；Manifest 与冷启动待补 | 5 |
| 20. [查询结果与失败诊断](query-diagnostics.md) | 失败原因和修复动作 | 粗状态已有；详细原因与作者动作待补 | 6 |
| 21. [Detour 查询缓存](query-cache.md) | 共享查询几何、版本与容量 | #1164 分支已实现；主线未收口 | 6 |
| 22. [作者工具链](authoring-toolchain.md) | 作者操作流程与统一命令 | 共用 Core 部分已具备；统一命令未完成 | 4 |
| 23. [NavMesh Showcase 交付](showcase-delivery.md) | 入口、主循环、证据与媒体 | NavGate 已实现，待运行验收 | 8 |
| 24. [旧类型与重复入口退役](legacy-retirement.md) | 迁移消费方后退役旧路径 | 命名已迁移；残余旁路待消费方迁移后删除 | 7 |

## 总 TODO 对应关系

下面沿用总合同最后一节的 8 组工作；实际实施按上表功能拆分，每个细项只有一个主文档。

| 总 TODO | 本组唯一主文档与独立子能力 |
|---|---|
| 1. 每板寻址 | [板身份、原点、瓦片隔离](board-addressing.md) |
| 2. 运行时合同 | [局部更新与路径刷新](runtime-dirty-rebake.md)、[快照/worker/代次/预算](runtime-bake-budget.md) |
| 3. 新源与语义 | [源接入](source-and-semantics.md)、[层/表面](multi-layer-pathing.md)、[坡度/台阶/净高](slope-clearance.md)、[结构障碍](structural-obstacles.md)、[区域代价](area-costs.md)、[水面](water-semantics.md)、[Link](navmesh-links.md) |
| 4. 作者工具链 | [统一作者流程](authoring-toolchain.md)、[profile 配置与半径](agent-profiles.md) |
| 5. 产物与编辑器 | [产物/Manifest/冷启动](artifacts-and-manifest.md)、[投影贴图](walkability-projection.md)、[真实调试显示](navmesh-debug-view.md) |
| 6. 查询性能与诊断 | [缓存/容量/零分配](query-cache.md)、[查询结果与错误动作](query-diagnostics.md) |
| 7. 旧类型清理 | [旧入口与消费方退役](legacy-retirement.md) |
| 8. Showcase 交付 | [统一交付合同与 NavGate](showcase-delivery.md)；每个功能的场景留在各自页面 |

## 全量能力对账

旧导航材料中出现的能力全部保留在目录中；新页面只拆责任，没有删除旧行为。下面是跨旧 SSOT、实现报告和新专题的索引，状态仍以各专题末节的证据为准。

| 原有能力 | 旧证据 | 新专题 | 当前口径 |
|---|---|---|---|
| 世界范围、Cell/Chunk/FlowWindow、米到厘米换算 | `architecture/spatial-scale-and-resolution-ssot.md`、`reference/map-scale-authoring-guide.md` | [空间尺度与分辨率](spatial-scale-and-resolution.md) | 规则已有，编辑器联动和全链约束未全收口 |
| Grid / Hex / NodeGraph 拓扑 | `reference/logic-terrain-and-topology.md`、`reference/nav-bake-budget-and-estimation.md` | [拓扑与逻辑地形](topology-and-logic-terrain.md) | Grid/Hex bake 已有，NodeGraph 走图路径，单源投影待补 |
| Graph/Mesh/Auto 路由、边投影、nearest/goal snap | `reference/routing-to-mass-execution.md`、`reference/graph-query-services.md` | [路由域选择与动态边权](route-domain-selection.md) | 选择器与共享图查询已有，作者面和动态边权待验收 |
| 动态 GraphEdgeCostOverlay | `reference/navmesh-authoring-bake-toolchain.md`、`reference/transport-network-asset.md` | [路由域选择与动态边权](route-domain-selection.md) | 缺 overlay 时必须失败，不能降级；完整运行链待收口 |
| Route → waypoint → MassNavigationFlow | `reference/routing-to-mass-execution.md`、`reference/move-planning-mass-navigation-flow-road-execution.md` | [路线到 MassNavigation 执行](route-execution.md) | 核心 sink 已有，双 profile 实机 UAT 待补 |
| full/dirty/window bake、校准、内存上界 | `reference/nav-bake-budget-and-estimation.md` | [烘焙估算、校准与内存预算](bake-estimation-and-memory.md) | 理论模型已有，统一 CLI 输出和本机校准待补 |
| projection / walkability PNG / nearest-poly | `reference/logic-terrain-and-topology.md`、`reference/navmesh-authoring-bake-toolchain.md` | [拓扑与逻辑地形](topology-and-logic-terrain.md)、[投影可行性贴图](walkability-projection.md)、[路由域选择与动态边权](route-domain-selection.md) | 分属 source、presentation、query 三个 owner，不能合成一个“投影系统” |
| Physics2D obstacle bridge | `reference/obstacle-authoring.md`、`reference/nav-domain-unification-epic-report.md` | [结构障碍足迹](structural-obstacles.md) | ECS obstacle provider 是唯一输入，运行时投影与 NavMesh 脏区仍需全链验收 |
| water sidecar / terrain areaId 投影 | `reference/nav-domain-configuration-migration-guide.md`、`reference/logic-terrain-and-topology.md` | [水面导航语义](water-semantics.md)、[区域分类与 Agent 通行代价](area-costs.md)、[烘焙源接入](source-and-semantics.md) | 语义、代价、投影分开维护，cost 不回写地形 |
| `.ntil`、Manifest、冷启动与版本拒载 | `reference/navmesh-authoring-bake-toolchain.md`、`reference/runtime-incremental-navmesh-rebuild.md` | [NavTile 产物与 Manifest](artifacts-and-manifest.md) | `.ntil`/Store 已有，完整 manifest 和冷启动链待补 |
| Web Editor 真实 Bridge、Estimate/Bake/Simulation | `reference/navmesh-authoring-bake-toolchain.md`、`reference/nav-domain-configuration-migration-guide.md` | [作者工具链](authoring-toolchain.md) | 共用 Core 部分已有，统一入口和旧 payload 退役待补 |

## 整合顺序

1. 先完成板身份与源输入，让后续产物和编辑器引用同一上下文。
2. 分别接收 `codex/nav-bake-policy` 的源/策略片段、#1164 的缓存/worker、#1006 的分类/去 Cost 和本地 #1402 的 AgentTypeId 路由修复；按对应功能页接入现有服务，不整支回迁。
3. 完成统一产物、加载与诊断合同，再将作者工作台、投影图和真实网格显示接到它们。
4. 多表面、水深、Link 各自补底层能力与独立场景；有基础字段不代表能力完成。
5. 各场景取得真实验收后，迁移剩余消费方并删除旧入口。

## 阅读与交付规则

- 章节顺序统一为概述、结构、详情（编辑器 → 工具 → Runtime）、场景与 Showcase、边界、UAT、现状与 TODO。
- Showcase 设计有自己的主循环、消融、图例、至少四个现场操作、API 缺口和 CucumberBDD 验收。配置修改后重启不能计作运行时旋钮。
- “设计完成”表示文档齐备；“已实现，待运行验收”表示代码/入口存在；“可玩交付完成”必须满足真实进程、操作和状态取证要求。
- 分支实现与主线完成分开记。各功能页列接收判断，具体分支列表在总合同末节维护。
