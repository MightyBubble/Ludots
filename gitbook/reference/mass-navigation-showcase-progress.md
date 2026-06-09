# Mass Navigation Showcase 进度说明

本文是 `mass-navigation-showcase-acceptance.md` 的执行进度页。

验收指南是目标合同，不能被执行过程、截图清单或临时报告反复改写。本文只说明当前代码做到什么、怎么配置、怎么运行、怎么判断通过，以及玩家和 Mod 开发者应该看哪些证据。

当前工作树状态：`IN_PROGRESS / NEEDS_MANUAL_UAT`。旧报告里曾出现过 `PASS / PRODUCTION_READY`，但那轮证据偏向自动关键帧和截图清单，不能代表现在这批“真实可操作 showcase”的最终验收。当前报告口径已纠偏：自动回放、截图、关键帧和机器诊断只能证明 `machine_production_evidence_success`，不能自动升级为生产 PASS；最终验收必须同时满足机器证据通过、`manual_uat_accepted=true`、Raylib playable window 能启动给验收人操作，并且玩家视角、Mod 开发者视角、专业交互视角复审均通过。

最近一次复查时间：`2026-05-13 15:40 +08:00`。本页是进度和阻塞项，不是通过证书。

## 最新纠偏

showcase 不是 slide show。验收对象必须是用户能操作的玩法或工具：

- 玩法场景：启动一个干净 entry mod，玩家框选、点地、切策略、下 order、观察部队移动与 debug presentation。
- 编辑器场景：启动 bake/editor workbench，工具用户生成或加载地图源，转换为 LogicHeightmap，烘焙 NavMesh，点选 path 起终点，编辑 layer/area，保存 patch/dirty chunks，再走正式 dirty rebake。
- 截图、关键帧、报告：只能作为某次操作后的证据，不能替代玩法或编辑器功能。
- 没有真实启动、操作和复审的用例，状态只能是 `READY_TO_TEST`、`SMOKE_ONLY`、`NEEDS_MANUAL_UAT` 或 `BLOCKED`，不能写成通过。
- 机器证据通过但还没有人工窗口验收时，`production_status` 必须是 `NEEDS_MANUAL_UAT`，不是 `PASS`。
- 只有存在人工 UAT 签收证据并且三视角复审通过时，`production_status` 才能写成 `PASS`。
- `-CaptureEvidence` 现在只表示“自动用户回放操作后截图”。玩法用例必须生成 `*-operation-trace.jsonl`，trace 里必须同时出现 `input`、`result`、`complete`，否则脚本失败。

## 怎么读

- `READY_TO_TEST`：代码已接入正式 runtime/tool 链路，可以启动对应 entry mod 或工具做人工验收。
- `CODE_CHAIN_TESTED`：已有自动测试证明关键代码链路走通，但还没有证明玩家能在完整窗口里完成操作，也没有证明 80/100 FPS。
- `SMOKE_ONLY`：有真实数据和诊断，但还没有覆盖完整玩家/编辑器交互闭环，不能当生产完成。
- `NEEDS_MANUAL_UAT`：机器证据、关键帧、trace 和报告已经能证明链路，但还缺人类验收者真实打开窗口操作并签收。
- `BLOCKED`：缺正式交互、缺生产链路、缺真实性能证据或缺复审。
- `Raylib playable`：离线截图不是验收终点，必须能打开 `mass_navigation` Raylib 窗口并操作 UI。
- `Raylib framebuffer`：截图只作为操作后的证据；不允许用截图替代玩法或编辑器功能。
- `Raylib framebuffer 截图`：用于记录某次真实操作之后的关键帧，不是 showcase 本体。
- `operation trace`：自动验收里的用户操作记录。它必须写清楚左键/右键/选择/下令/编辑等输入，哪个正式系统消费输入，以及得到的 route/order/slot/flow 输出。

## 当前完成度总览

| 区域 | 状态 | 已完成 | 还差什么 |
| --- | --- | --- | --- |
| Clean entry mods | READY_TO_TEST | U01-U16 每个都有独立 asset-only root mod，依赖 `MassNavigationMod`，`panelMode=Focused` | 需要逐个真实启动、操作、截图、复审 |
| Focused UI 操作语义 | CODE_CHAIN_TESTED / NEEDS_VISUAL_REVALIDATION | focused panel 和屏幕 overlay 均改为 operation cockpit：显示 `Use case body`、`User operation`、`Live output`、`Acceptance signal`、`Production chain`，并由测试禁止 `Now/Do/Look/Pass` 式讲稿回退 | 仍需要逐个窗口启动截图确认排版可读，尤其是 U05/HPA、U16/workbench 和 U12/10k flow |
| Playable operation replay | CODE_CHAIN_TESTED | `MassNavigationShowcaseReplaySystem` 只在 `LUDOTS_MASS_NAV_REPLAY_USECASE` 开启时运行；它通过 `SelectionRuntime`、`AuthoritativeInput`、`MassNavigationPathPreviewInputSystem`、`MassNavigationCommandBridgeSystem`、`MassNavigationOrderBridgeSystem`、`NavGroupRuntime`、`MassFlow` 回放 U04/U05/U06/U07/U08/U10/U11/U12/U13/U14/U15 并写 `operation-trace.jsonl` | 这是自动证据链，不替代人工可玩验收；U05/U06/HPA 仍需真实 route sequence 视觉；U13 仍缺单位绕 40k baked obstacle 的端到端玩法 |
| NavMesh bake workbench | READY_TO_TEST / SDK_BLOCKED | Raylib bake workbench 支持 1-5 切换覆盖、tile、path、HPA、layer/area；path view 支持左键起点/右键终点；layer/area view 支持 brush 操作并写出 `logic-heightmap-edit-patch.json`、`dirty-chunks.json`，再由 `Ludots.Tool map patch-lhtm` 和 `nav bake-recast-lhtm --dirty` 进入正式链路 | 还缺从 mod-authored asset/config 直接启动的大型异步 bake 合同、完整可视编辑 UX、运行时 mutation 策略和人工操作录制 |
| HPA 证据口径 | SMOKE_ONLY | 生产门改为 active-window portal graph，`UsesSyntheticMacroGridTarget=false` | UI 仍需要读取真实 route sequence 并高亮起点 chunk、终点 chunk、编号经过 chunk、portal crossing、streamed-out chunk |
| 10k flow / target allocation | CODE_CHAIN_TESTED / NEEDS_MANUAL_UAT | U08/U12 已验证 `Select 10k Army -> right-click -> GAS OrderBuffer -> MassNavigationOrderBridge -> NavGroupRuntime -> MassFlow targets`，并接 runtime diagnostics；`-CaptureEvidence` 会留下选择、右键、order bridge、target refresh/flow trace；U08 最近一次机器证据显示 10k slots/reachable、blocked=0、fallback=0、Raylib 约 115 FPS | 还缺用户手动框选/点击的完整窗口复验、moving/settled/stuck 全链路可视和整局 80/100 FPS full-game 复验 |
| 40k static obstacles | SMOKE_ONLY | planned/authored/baked/loaded 和 solver-active subset 分开诊断 | 还缺 obstacle 对 graph/navmesh/flow cost 的端到端交互证明 |
| 性能 | BLOCKED | Raylib micro benchmark 曾给出高 FPS 范围证据 | 还不能 overclaim full-game 64km + 10k + 40k + debug-on 80/100 FPS |
| Subagent 复审 | BLOCKED | 旧复审已指出玩家/SDK/交互问题 | 新操作式 showcase 完成启动截图后，需要三视角重新复审 |

## 三视角复审结论

本轮只读复审没有给通过结论，结论如下：

| 视角 | 结论 | P0 阻塞 |
| --- | --- | --- |
| 玩家视角 | U04/U08/U12 和 Raylib editor 入口最接近真实可操作；U05/U06/U07/U10/U11/U13 仍偏按钮/overlay，需要继续做操作闭环 | 这些用例不能只点按钮看说明，必须用真实单位、真实 order、真实 path query 或真实编辑器操作闭环 |
| Mod 开发者视角 | U01-U16 entry mod 结构干净，asset-only 方向正确 | Editor runner 仍以 8x8 `mountainRiver` 临时 fixture 为主，不能证明开发者自己的 mod-authored map/config 可直接 bake；缺异步 bake job/progress/cancel/dirty queue/rollback 合同 |
| 交互设计视角 | 5W1H 数据已在 guide 中，但界面还不像任务流 | HPA 必须显示真实 route chunks/portals；NavMesh 必须显示真实 walkable/blocked/high-cost/edge/agent radius/corridor/portal/link/layer 编辑语义；10k flow 必须显示 commanded/moving/settled/stuck 和采样数量 vs 真实数量 |

## 自动证据不等于玩法本体

`MassNavigationShowcaseReplaySystem` 是验收机器人，不是 showcase 本体。它只在下面这些环境变量存在时启动：

```powershell
$env:LUDOTS_MASS_NAV_REPLAY_USECASE = "U12"
$env:LUDOTS_MASS_NAV_REPLAY_TRACE_PATH = "artifacts\acceptance\mass-navigation-usecases\u12\U12-operation-trace.jsonl"
$env:LUDOTS_MASS_NAV_REPLAY_FRAME_START = "45"
```

回放原则：

- path-only / HPA / strategy / waypoint：向 `AuthoritativeInput` 和 `AuthoritativePointerButtons` 写入左键/右键，交给 `MassNavigationPathPreviewInputSystem` 调 `PathService` 和 `PathStore`。
- order reuse / target allocation / 10k flow：先通过 `SelectionRuntime.ReplaceSelection(..., LivePrimary, ...)` 产生正式选择集，再写入右键命令，由 `MassNavigationCommandBridgeSystem` 提交 `OrderBuffer`，由 `MassNavigationOrderBridgeSystem` 写入 `NavGroupRuntime`，最后由 `MassFlow` 刷新目标和 flow。
- static obstacle / performance / debug budget：只记录真实 runtime diagnostics 和 overlay 状态，不把它们写成玩家玩法通过。
- 普通人工启动不设置这些环境变量，玩家仍然要自己点按钮、框选、右键、切视图、编辑 layer。

trace 通过的最低标准：

```text
input  -> 用户输入，例如 left_click_start / right_click_destination / select_10k_army
system -> 正式系统产物，例如 order_bridge_after_large_selection_order / target_refresh_and_flow_smoke
result -> 这次操作的输出，例如 pathpoints、route chunks、route bucket、slots、flow targets
complete -> 用例回放结束
```

如果一个用例只有 screenshot，没有 operation trace，不能算玩法验收证据。

## SDK 接入路径

一个 0 上下文 Mod 开发者先看这些文件：

| 文件 | 作用 | 需要改什么 |
| --- | --- | --- |
| `mods/capabilities/navigation/MassNavigationMod/assets/game.json` | 把 `startupMapId` 指到 `mass_navigation` | 自己 Mod 可以换成自己的地图 id |
| `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json` | 地图入口，声明 visual heightmap 和 NodeGraph board | 换地图尺寸、board、VisualHeightmapAsset |
| `mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json` | 大世界、流送、10k agent、40k obstacle、flow 和 bake 目标 | 调 `macroChunkColumns/Rows`、`targetStaticObstacleCount`、hot zones、solver window |
| `mods/capabilities/navigation/MassNavigationMod/assets/Configs/Navigation/navmesh.json` | NavMesh profile、layer、area/cost 定义 | 增加 Ground/Water/Air/Mountain 等 layer，调整 agent radius 和 slope/climb |
| `mods/capabilities/navigation/MassNavigationMod/assets/Configs/Navigation/pathing.json` | agent type 到 graph/navmesh/hybrid 策略的绑定 | 给 Infantry/LargeVehicle/Mountain/Naval/Air 配 `profileId`、`layer`、cost、graph tags |
| `mods/capabilities/navigation/MassNavigationMod/assets/Data/Nav/mass_navigation/nav-bake-diagnostics.json` | bake 后的机器可读诊断 | 看 active window、layer/profile 覆盖、失败/缺失/脏块 |
| `mods/capabilities/navigation/MassNavigationMod/assets/Data/Nav/mass_navigation/**/*.ntil` | Recast nav tile 产物 | 只由 bake 工具生成，不手写 |

## Clean showcase entry mods

`MassNavigationMod` 是能力和 SDK 基底。每个 UAT 用例都有一个干净 entry mod：它只依赖 `MassNavigationMod`，只覆盖 `assets/game.json` 和 `assets/MassNavigationConfig.json` 的 `showcase` 区块，`panelMode=Focused`，不复制导航逻辑。

启动单项 showcase：

```powershell
.\scripts\run-mod-launcher.cmd cli launch mod:MassNavigationU05WorldHpaRouteShowcaseMod --adapter raylib --build never
.\scripts\run-mod-launcher.cmd cli resolve mod:MassNavigationU16BakeToolQueryShowcaseMod --adapter raylib --build never
```

推荐用统一入口脚本启动真实用例：

```powershell
# 玩法用例：真实启动一个干净 mod。CaptureEvidence 会先做操作回放、写 trace，再记录操作后的证据。
.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U08 -CaptureEvidence

# 编辑器用例：生成/转换 LogicHeightmap，烘焙 .ntil，打开 workbench 证据，并应用 layer patch 后 dirty rebake。
.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U03 -EditorApplyPatch
```

玩家视角看 `Player sees`：这一步点什么、画面应该出现什么、为什么能说明功能 work。Mod 开发者视角看 `Mod author checks`：这一步依赖哪些配置、数据烘焙和诊断字段，怎么迁移到自己的 Mod。

| 用例 | Entry mod | 主操作 | 当前状态 | 必须看到 |
| --- | --- | --- | --- | --- |
| U1 | `MassNavigationU01VisualHeightmapBakeShowcaseMod` | `Bake VHTM Window` | READY_TO_TEST | visual heightmap -> LogicHeightmap -> `.ntil` 活动窗口 tile |
| U2 | `MassNavigationU02LogicHeightmapBakeShowcaseMod` | `Inspect Logic Bake` | READY_TO_TEST | vtxm/vhtm/quad/hex vertex 收敛到 LogicHeightmap |
| U3 | `MassNavigationU03LayerAreaEditorShowcaseMod` | `Layer Paint Inspect` | READY_TO_TEST | 山、河、NoFly、blocked、高权重区与 mesh sample 同屏；workbench 可保存 layer edit patch 和 dirty chunks |
| U4 | `MassNavigationU04PathOnlyQueryShowcaseMod` | `Pick Path Preview` | READY_TO_TEST | 只选起点/终点高亮路线，`order_delta=0` |
| U5 | `MassNavigationU05WorldHpaRouteShowcaseMod` | `Pick HPA Route` 后左键起点、右键远端目标 | READY_TO_TEST / VISUAL_SMOKE | 64km、active window、起终点 chunk、编号 route chunks、portal crossing；自动 trace 记录 path query 更新 HPA diagnostics |
| U6 | `MassNavigationU06StrategySwitchShowcaseMod` | `Pick Strategy Route` 后左键起点、右键目标 | READY_TO_TEST / VISUAL_SMOKE | 同一起终点比较 RoadGraph、NavMesh、Hybrid 候选；自动 trace 记录同一 query 输入 |
| U7 | `MassNavigationU07OrderReuseShowcaseMod` | `Select Reuse Squad` 后右键同点两次、近点一次 | CODE_CHAIN_TESTED | 同点/近点命令复用 route bucket 和 route id；自动 trace 记录正式选择集、右键 order、order bridge |
| U8 | `MassNavigationU08TargetAllocationShowcaseMod` | `Select 10k Army` 后右键目的地 | CODE_CHAIN_TESTED / NEEDS_MANUAL_UAT | 正式选择集 + 右键 order 展开成 10k reachable slot cloud；自动 trace 记录正式 order chain。最近机器证据可证明 selected=10000、slots=10000、reachable=10000、blocked=0、fallback=0，但还不是人工 UAT PASS |
| U9 | `MassNavigationU09LayerCostsShowcaseMod` | `Layer Cost Matrix` | READY_TO_TEST | ground/water/air/mountain cost 与 forbidden area 同屏；layer edit patch 可进入 dirty rebake |
| U10 | `MassNavigationU10WaypointAuthoringShowcaseMod` | `Edit Waypoint Plan` 后选起点/目标，再点新 midpoint | CODE_CHAIN_TESTED | waypoint 可编辑，pathpoint 是不可变 query output；自动 trace 记录旧 pathpoints invalidated 和新 pathpoints |
| U11 | `MassNavigationU11LargeWorldStreamingShowcaseMod` | `Active Window` | READY_TO_TEST / VISUAL_SMOKE | 64km、256x256 macro chunks、loaded active window、streamed-out 计数；自动 trace 记录相机跳转后 loaded chunks |
| U12 | `MassNavigationU12TenKFlowShowcaseMod` | `Select 10k Army` 后右键目的地 | CODE_CHAIN_TESTED / SMOKE_ONLY | 正式 order 产生 10k shared command、slots、flow movement counters；自动 trace 已覆盖选择、右键、OrderBridge、TargetRefresh、MassFlow targets |
| U13 | `MassNavigationU13StaticObstacleWorldShowcaseMod` | `40k Obstacles` | SMOKE_ONLY | 40k planned/authored/baked/loaded 与 solver-active subset 分开；还缺玩家单位绕障操作 |
| U14 | `MassNavigationU14PerformanceDebugShowcaseMod` | `FPS Budget` | BLOCKED | full-game renderer/data scope 的 80/100 FPS 证据 |
| U15 | `MassNavigationU15DebugVisualBudgetShowcaseMod` | `Debug Budget` | READY_TO_TEST | debug layer 可开关、采样、有预算 |
| U16 | `MassNavigationU16BakeToolQueryShowcaseMod` | `Bake Query Tool` | READY_TO_TEST | source lanes、LogicHeightmap、NavMesh tile、interactive path query、layer patch、dirty rebake、HPA/result JSON |

## 架构链路

```text
Mod 配置
  -> VisualHeightmap / VertexMap / LogicHeightmap source
  -> LogicHeightmap 统一语义层
  -> Recast tile bake 生成 .ntil + artifact + nav-bake-diagnostics.json
  -> NavTileStore / NavQueryService 提供 active-window NavMesh query
  -> HPA graph diagnostics 标出 macro chunk、portal、corridor
  -> pathing.json 决定 RoadGraph / NavMesh / Hybrid 策略
  -> Waypoint order intent 和 PathPoint query result 分离
  -> order reuse 和 target allocation 复用路线桶
  -> MassFlow solver / flowfield 避障移动
  -> Launcher Evidence / Raylib overlay 输出 UAT、截图、报告
```

关键概念：

- Waypoint 是可编辑的计划移动或业务路线，例如商贸路线、巡逻路线、一串 order intent。
- PathPoint 是某一次 query 得到的不可变底层路径点，一个 order 内使用它执行移动。
- path-only preview 只选起点和终点，高亮路线，不提交 order，`order_delta=0`。
- Road graph、NavMesh、Hybrid 是寻路策略；单位怎么走由 flowfield、formation slot、避障和 steering 处理。
- 同点或近点 order/path 会归一化到 route bucket，复用 route id 和 path/mesh signature。
- 框选大部队点一个目标，target allocation 会把单点展开为大量 reachable slots，避免 10k 单位挤到同一点。
- Air/Water/Mountain/Ground 通过 `layer`、`profileId`、area cost、graph tag rules 分开表达。

## 运行方式

```powershell
dotnet test src\Tests\PresentationTests\PresentationTests.csproj --filter "MassNavigationShowcaseAcceptanceDocumentTests|MassNavigationPlayableShowcaseTests|MassNavigationBakeDataDiagnosticsTests" -v:minimal
dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj --filter "NavBakeDiagnosticsContractTests" -v:minimal
dotnet build src\Tools\Ludots.Launcher.Evidence\Ludots.Launcher.Evidence.csproj -v:minimal
dotnet build src\Tools\Ludots.Launcher.Cli\Ludots.Launcher.Cli.csproj -v:minimal
.\scripts\run-mod-launcher.cmd cli launch mod:MassNavigationU08TargetAllocationShowcaseMod --adapter raylib --build never
.\scripts\acceptance\run-mass-navigation-showcase-acceptance.ps1 -OutputRoot artifacts\acceptance\mass-navigation-showcase-current -Adapter raylib
```

`-StopOnFailure` 只在最后要阻断 CI 或声明生产验收时使用。当前阶段应先不带 `-StopOnFailure`，让 suite 产出 `IN_PROGRESS / NEEDS_REVALIDATION` 报告和失败清单。

## 单用例操作 Runbook

| 用例 | 类型 | 启动命令 | 用户要做什么 | 操作后应该看到 |
| --- | --- | --- | --- | --- |
| U01 | Editor | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U01` | 打开 workbench，看 vhtm -> LogicHeightmap -> `.ntil` | 覆盖率、walkable/blocked/high-cost、agent radius、tile sample |
| U02 | Editor | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U02` | 比较 logic source 和 bake output | quad/hex/vhtm/vtxm 都收敛到 LogicHeightmap 语义 |
| U03 | Editor | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U03 -EditorApplyPatch` | 在 layer view 用 Q/W/E/R/B 选 brush，左键绘制，S 保存 | `logic-heightmap-edit-patch.json`、`dirty-chunks.json`、edited `.lhtm`、dirty rebake `.ntil` |
| U04 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U04 -CaptureEvidence` | 打开 Path Preview，用例内只看路径，不发 order | 高亮 pathpoints/corridor/portal，`orderDelta=0` |
| U05 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U05 -CaptureEvidence` | 打开 World/HPA | 64km、256x256 chunk、active window、编号 crossed chunks、portal crossing |
| U06 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U06 -CaptureEvidence` | 点击 Strategy | 同起终点 RoadGraph/NavMesh/Hybrid 三种候选及 cost |
| U07 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U07 -CaptureEvidence` | 点击 Select Reuse Squad，右键同一个目标两次，再右键附近目标 | route bucket、cache hit、route id/signature 复用 |
| U08 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U08 -CaptureEvidence` | 点击 Select 10k Army，然后右键一个目的地 | 一个正式 RTS 右键 order 变成 10k reachable slots，blocked/fallback 分开 |
| U09 | Editor | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U09 -EditorApplyPatch` | 检查 layer cost，并保存一次 layer patch | ground/water/air/mountain cost、NoFly/blocked、高权重区、dirty rebake |
| U10 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U10 -CaptureEvidence` | 点击 Waypoint Edit | waypoint 可编辑，old pathpoints invalidated，新 pathpoints 由 query 再生成 |
| U11 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U11 -CaptureEvidence` | 看 full map / active window | 64km 世界、256x256 chunks、loaded/notLoaded 工作集 |
| U12 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U12 -CaptureEvidence` | 点击 Select 10k Army，然后右键目的地 | 10k commanded、GAS active order、OrderBridge order group、flow enabled、slot/movement counters |
| U13 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U13 -CaptureEvidence` | 点击 40k Obstacles | planned/authored/baked/loaded=40k，solver-active subset 单独显示 |
| U14 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U14 -CaptureEvidence` | 看 FPS Budget | 当前 renderer scope、p95/p99、是否 full loaded data measured |
| U15 | Playable | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U15 -CaptureEvidence` | 开关 debug layer | debug draw 有采样、有预算，不进 hot path |
| U16 | Editor | `.\scripts\acceptance\run-mass-navigation-usecase.ps1 -UseCase U16 -EditorApplyPatch` | 在 workbench 切 1-5：coverage/tile/path/HPA/layer，左/右键改 path，layer 里保存 patch | source lanes、LogicHeightmap、NavMesh tile、interactive path、HPA、layer patch、result JSON |

这些命令只是“可启动/可记录”的入口。真正通过仍需要人工操作、截图审阅、性能复验和三视角 subagent 审核。

Raylib framebuffer 证据运行方式：

```powershell
$env:LUDOTS_TAKE_SCREENSHOT_PATH = "artifacts\acceptance\mass-navigation-showcase-current\focused-entry-u08\u08-framebuffer.png"
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = "240"
$env:LUDOTS_AUTO_EXIT_FRAME = "300"
$env:LUDOTS_RAYLIB_DIAGNOSTIC_PATH = "artifacts\acceptance\mass-navigation-showcase-current\focused-entry-u08\u08-diagnostic.log"
$env:LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES = "60"
.\scripts\run-mod-launcher.cmd cli launch mod:MassNavigationU08TargetAllocationShowcaseMod --adapter raylib --build never
```

## 验收矩阵

| 场景 | 当前完成度 | 玩家要做什么 | 必须看到什么 | 机器门 |
| --- | --- | --- | --- | --- |
| S1 64km 世界加载 | READY_TO_TEST | 打开 U11 / U5，看世界、小地图、active window | `64km x 64km`、`256x256 macro chunk`、`loaded_chunk_count`、边界和 ground picking 结果 | `world_boundary_diagnostics.CameraInBounds=true` |
| S2 远距离路网移动 | SMOKE_ONLY | 点 World/HPA 或 Strategy | 编号 chunk 路线、portal、Road/NavMesh/Hybrid 候选 | `hpa_graph_diagnostics.ActiveWindowRouteAvailable=true` |
| S3 NavMesh 最后一公里 | READY_TO_TEST | 点 NavMesh tile / Path Preview | walkable triangles、blocked/high-cost、agent radius、portal/corridor、pathpoints | `active_window_navmesh_query` rows 为 Ok |
| S4 10k 同屏群体移动 | SMOKE_ONLY | Select 10k Army 后右键目的地 | 10k commanded、moving/settled、flow、slot cloud | `commanded_agents>=10000` 且 `moving+settled>=10000` |
| S5 40k 静态障碍 | SMOKE_ONLY | 点 U13 Obstacles | 40k planned/authored/baked/loaded、solver-active subset | `LoadedStaticObstacleCount>=40000` |
| S6 Flowfield 开启 | SMOKE_ONLY | 点 U12 Flow | shared route、flow enabled、slot allocation 与 movement 分开显示 | `flow_enabled=true` |
| S7 多热点大世界 | BLOCKED | minimap 跳远点再跳回 | hot zone、camera 不重置、scenario spawn/reset 稳定 | `ScenarioSpawnCount=1` 且 `SceneResetCount=0` |
| S8 诊断默认关闭 | READY_TO_TEST | 点 U15 Debug / U14 FPS | overlay bounded、runtime overlay writes 为 0、Raylib A/B 预算 | `debug overlay sampled and bounded` |
| S9 一键 UAT 录制 | NEEDS_MANUAL_UAT | 跑 acceptance suite | summary、report、trace、path、screens、manifest 全部生成 | 当前应输出 `IN_PROGRESS / NEEDS_REVALIDATION` 或 `IN_PROGRESS / NEEDS_MANUAL_UAT`；只有人工签收后才允许 `PASS / PRODUCTION_READY` |

## U1-U16 必看

每个用例截图必须是操作后的证据，而不是预先摆好的说明图。截图里必须能读到 `Use case body`、`User operation`、`Live output`、`Acceptance signal`、`Debug meaning`、`Production chain`，并且 focused panel 里必须有具体操作按钮。

- U1/U2/U16：VisualHeightmap、vertex map、logic heightmap 都统一到 LogicHeightmap，再 bake 成 `.ntil`。
- U3/U9：山地、河流、NoFlyZone、高权重区、ground/water/air/mountain layer 必须可视化。
- U4/U10：waypoint 是可编辑 order intent，pathpoint 是不可改 query result。
- U5/U11：HPA 经过哪些 chunk 必须编号高亮，portal/corridor 要能看懂。
- U6：同一个起终点可以切 graph/navmesh/hybrid，看 selected strategy、route id、cost、mesh source、touched tiles。
- U7/U8：同点/近点 order 要复用 route bucket；框选 10k 后一个目标要分配成 10k reachable slots。
- U12/U13：10k flow 和 40k obstacles 是运行时体验证据，必须和 bake/data 证据分开读。
- U14/U15：性能 debug 视觉必须有预算，不能为了诊断牺牲帧率。

## 关键字段速查

- `loaded_chunk_count`
- `boundary_click_result`
- `ground_picking_result`
- `world_boundary_diagnostics`
- `hpa_graph_diagnostics.ActiveWindowRouteAvailable`
- `hpa_macro_diagnostics.UsesSyntheticMacroGridTarget=false`
- `active_window_navmesh_query`
- `acceptance_proof`

## 通过定义

验收不是“有截图”。通过必须同时满足：

- `machine_production_evidence_success = true`
- `manual_uat_accepted = true`
- `production_gate_success = true`
- `production_blocked_use_case_count = 0`
- `production_gate_failed_checks` 为空
- U1-U16 `production_status=PASS`
- U1-U16 均有 `acceptance_proof`
- 所有关键帧都是操作后的证据，能从玩家语言读懂输入、输出和原因
- `mass-navigation-live-window.png` 或 `mass-navigation-raylib-framebuffer.png` 不是白屏，能看到 Raylib playable operation panel
- 三个 subagent 分别从玩家、Mod 开发者、专业交互设计师角度复审通过

如果机器证据通过但 `manual_uat_accepted=false`，正确状态是 `NEEDS_MANUAL_UAT`，不是 `PASS`。这时可以说“链路可回放、证据可读”，但不能说“全部验收通过”。
