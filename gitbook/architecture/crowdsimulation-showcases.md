# CrowdSimulation 演示场景

七个可玩的演示，都在种子 1337 的 S1 地图上，单位和建筑模板用 `CrowdSimulationDemoContentMod`。每个场景是一个纯数据 Mod，只写自己的指令脚本、标题和镜头；框选、右键下令、HUD、玩家视图都来自 S4 部署演示。进图后脚本自己往下跑，玩家随时可以框选单位另下命令。

## 场景

| 预设 | 场景 Mod | 演示什么 |
|---|---|---|
| `crowd_simulation_showcase_march_raylib` | `CrowdSimulationMarchShowcaseMod` | 120 人方块在图中心集结（约 443 tick），tick 450 向东行军，tick 480 改令转向约 60° |
| `crowd_simulation_showcase_reversal_raylib` | `CrowdSimulationReversalShowcaseMod` | 方块向东出发，tick 3 掉头往西，tick 46 同向再令一次 |
| `crowd_simulation_showcase_repeat_click_raylib` | `CrowdSimulationRepeatClickShowcaseMod` | 方块集结后，从 tick 450 起每 10 tick 对同一目标再点一次，共 8 次 |
| `crowd_simulation_showcase_fog_explore_raylib` | `CrowdSimulationFogExploreShowcaseMod` | 视野外的敌方小队、敌方目标和南边的新建筑都不画；tick 80 有 5 个敌人出现在本方旁边；tick 110 本方向南探索，约 tick 2689 看见建筑 |
| `crowd_simulation_showcase_fog_reroute_raylib` | `CrowdSimulationFogRerouteShowcaseMod` | 8 名步兵从陆地 (378125, 359375) 开往正北 (378125, 659375)。直线上格 (60,103) 是还没探索过的水域，岸上格 (60,102) 在 tick 8 放一栋边长 6250 cm 的建筑。开乐观时领队路径先直穿这格水，看见建筑和真实水域后改道并到达；关乐观时第一条路径就绕开这格水 |
| `crowd_simulation_showcase_fog_shared_vision_raylib` | `CrowdSimulationFogAllyVisionShowcaseMod` | 玩家 1 原地不动，盟友玩家 3 去看东边的建筑（约 tick 185 看见）、再往西路过敌人（约 tick 609）；tick 800 拆掉建筑，玩家 1 这边留残影，盟友回去后约 tick 1281 残影消失 |
| `crowd_simulation_showcase_stress_50k_raylib` | `CrowdSimulationStress50kShowcaseMod` | 和 50k 门禁同一场景：开局 5 万步兵，tick 2 四方都开往 (1200000, 800000)；遥测面板默认打开 |

迷雾场景的玩家视图按玩家 1 的视野组隐藏敌方。迷雾开着时（0 键切换），路线视图和 NavMesh 视图不画地形调色板，地面只贴本机座位的迷雾：没去过的格子黑，去过的格子蓝灰，看得见的格子是原地形色。迷雾贴花铺满整张地图：视野组激活时迷雾场按地图边长建好整块格子，投影器的范围就是这一整块，从没看见过的角落也读作“没去过”，画成黑色。5 万单位场景不隐藏，四方都画出来。

## 启动

```bash
dotnet build mods/LudotsCoreMod -c Release
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release -- launch preset:crowd_simulation_showcase_reversal_raylib
```

换成表里的其它预设名即可。启动器界面里按标题“CrowdSimulation 演示 · …”找。

常用键沿用 S4：左键框选，右键下令，P 暂停，PgUp / PgDn 倍速，F10 开关遥测面板（Sim 一行是每 tick 的模拟耗时；渲染一行只计界面层，不含三维场景），0 开关迷雾隐藏。`]` 切换乐观迷雾偏好（HUD「视野规则」里的同一项）；下一笔拖拽成阵或右键下令把这个偏好写进 `fogTerrain`。完整键位在 HUD 第一行。

## S4 部署演示

`CrowdSimulationS4DeployMod` 是种子 1337 上的交互部署场景：进图按 `deploy.initialUnits` 部署开局单位，并到点自动回放对拍。HUD 显示当前工具、避让频率、暂停/倍速、重烘切片、tick、单位状态分布、最近一笔重烘报告（脏 tile、代价/可通行变化、缓存命中、重规划、耗时）和回放结论。进入交互后自动回放停用；暂停时 tick 不推进但指令仍执行；倍速走 TimeFlow 的 Simulation 缩放；重烘脏 tile 会闪烁。

键位在 `mods/showcases/crowd_simulation/CrowdSimulationS4DeployMod/assets/Input/default_input.json`，HUD 第一行也有摘要。常用项：N 关闭工具、K 爆炸、G 生成、H 建筑、J 拆除、F 部署、R 散开、Y 满员、T 清缓存、`-`/`=` 避让频率、P 暂停、PgUp/PgDn 倍速、V 切视图、B 建造开关、X 拆除光标处、1–6 选结构模板、左键按当前工具提交。方形、圆、固定道路、两点连线都走 `placeStructure`。演示脚本可含迷雾命令（`reveal` / `obscure` / `forget` / `fogTerrain`），单位按认知改道；调试 HUD 读玩家 1 的可见/探索/残影格（`fogCells vis/explored/ghost`）。

## S3 路径演示

`CrowdSimulationS3PathMod`：左键设起点、右键设终点；Q 切换体型，T 在线程 1/2/4 间切换，G 放慢后台路径。画面显示折线、方向贴花，以及请求帧 / 生效帧 / 等待次数。

## 组成

- `CrowdSimulationShowcaseStageMod`：所有场景共用的舞台。依赖 S4 部署演示，把开局部署人数设为 0，清空 S4 的演示脚本，关掉自动回放，玩家视图按迷雾隐藏敌方。
- 场景 Mod：`assets/CrowdSimulationDebug.json` 写 `session.script` 和标题，`assets/Camera/virtual_cameras.json` 按 id 覆盖 `CrowdSimulation.Camera.S4Deploy` 的镜头位置、距离和角度（行军、折返、连点和三个迷雾场景用俯角 80°、偏航 180°，东在右；目标点偏下放，单位不压在左上角的面板下。迷雾探索把镜头拉到 2,900,000 cm、目标 (10000, 315000)，整张地图连同四边都在画面里，能看出迷雾盖到地图边缘。乐观迷雾把镜头收到距离 800,000 cm、目标 (378125, 509375)，对着这段南北向的穿越），`assets/game.json` 指向 `crowd_simulation_s1337`。5 万单位场景另外打开遥测面板、关掉迷雾隐藏。
- 脚本可以引用文件：`session.scriptAsset` 写 mod 内相对路径，指向根上有 `script` 数组的文档（和回归场景同一种格式）。会话先跑这份文件里的行，再跑 `session.script`，合起来仍须按 tick 升序。行军、折返、连点三个场景共用的 120 人开局写在舞台 Mod 的 `assets/CrowdSimulation/showcase/formation-120.json`，三个场景只写自己的指令。5 万单位场景直接引用 50k 门禁读的 `assets/CrowdSimulation/regress/stress-50k.json`，演示和门禁是同一个文件。
- 行军和连点的第一条指令都是 tick 0 走到 A 点，这一行没有抽出来：它是各自场景的第一步，连点场景后面八次点击也是同一个 A 点，抽成共享文件反而要拆成两层引用。

## 验收

`src/Tests/CrowdSimulationTests/Showcases/` 下的测试按启动器同样的 Mod 链起真实引擎、进图，让会话宿主自己跑脚本，在脚本指令的 tick 上核对对应 UAT 的不变量和阈值：

- `FormationShowcaseTests`：行进 UAT（连点逆向单位-tick ≤ 3%、人均翻转 ≤ 2 次；转向逆向 ≤ 2%）和折返 UAT（纵深 ≥ 4 格时掉头，单位相对位移 ≤ 纵深/2 + 1 格，mirror 继承）。场景坐标按 UAT 的格数在本会话导航上重算后与脚本比对。
- `FogShowcaseTests`：隐藏 UAT、乐观迷雾（认知导航直穿未探索水域，看见后改道并到达）和关闭乐观（真相导航从第一条路径起绕开水域并到达）、UAT-FOG-01/02。共享视野场景每 tick 跑一次仿真校验。
- `Stress50kShowcaseTests`：场景引用的文件就是回归目录里 stress 场景登记的那份，推过 40 tick 预热后单位数、迷雾刷新和遥测读数正常。不计时，不替代门禁。

```bash
dotnet test src/Tests/CrowdSimulationTests -c Release --filter "FullyQualifiedName~CrowdSimulationTests.Showcases"
```

真实窗口截图和录像在 `artifacts/acceptance/crowdsimulation-showcases/`，用 Raylib 客户端加 `mod:AgentBridgeMod` 拍：进图后暂停，每次步进 10 tick，到点截图。图里 HUD 第三行的 tick 是会话 tick。

| 场景 | 文件（会话 tick） |
|---|---|
| 折返 | `reversal.png`（16，已掉头）、`reversal_leg2_final_order.png`（48）、`reversal_leg3_march_max.png`（288）、`reversal.mp4`（0–300，10 帧/秒） |
| 行军 | `march_leg1_a.png`（300）、`march_leg2_b.png`（472）、`march.png`（712，转向后） |
| 连点 | `repeat_click_leg1_settling.png`（300）、`repeat_click_leg2_clicks.png`（480）、`repeat_click.png`（630） |
| 迷雾探索 | `fog_explore_before.png`（60）、`fog_explore_enemy_in_view.png`（150）、`fog_explore_seen.png`（2760） |
| 乐观迷雾 | `fog_reroute_optimistic.png`（开：直穿未探索水域）、`fog_reroute_known.png`（关：一开始就绕开已知水域） |
| 共享视野 | `fog_shared_vision_before.png`（20）、`fog_shared_vision_seen.png`（226）、`fog_shared_vision_enemy_shown.png`（642）、`fog_shared_vision_ghost_kept.png`（1032）、`fog_shared_vision_ghost_cleared.png`（1332） |
| 5 万单位 | `stress_50k_hud_paused.png`（134，暂停步进）、`stress_50k_hud.png`（149，实时） |

5 万单位场景在 Mesa llvmpipe 软件渲染下，遥测面板的 Sim 读数是 21.0–25.5 ms，FPS 0.6，也就是一帧约 1.7 s，几乎全花在软件渲染上。这个数不代表 50k 门禁的计时。

## 50k 帧时：Web 与 C# 对照

同一场景文件 `assets/CrowdSimulation/regress/stress-50k.json`（演示与 `CrowdKernel50KBenchmarkTests` 共用）。计时只含仿真步进（运动、迷雾、认知、校验码），不含 Raylib 渲染。

| 端 | 命令 | 典型环境（4 vCPU / 15 GiB，Release） |
|---|---|---|
| C# | `dotnet build mods/LudotsCoreMod -c Release` 后 `dotnet test src/Tests/CrowdSimulationTests -c Release --filter "FullyQualifiedName~CrowdKernel50KBenchmarkTests"` | 产物 `artifacts/benchmarks/crowd-kernel-50k-tick/trace.jsonl`（含 avg / p50 / p95 / max）；硬门 P95 &lt; 33 ms 仅 Release |
| Web | 沙盒根目录 `node scripts/benchmarkStress50k.mjs --warmup 40 --measured 200`（脚本正本 `tools/crowdsimulation-export/benchmarkStress50k.mjs`） | 产物 `artifacts/benchmarks/crowd-stress50k-web/web-timing.json`（`tickAvgMs` / `tickP95Ms`）；墙钟随机器变化，与 C# 比趋势不比逐位 |

测量前读 `/proc/loadavg`：1 分钟负载高于核数时 C# 门禁会最多等 180 s 再开窗（见 `CrowdKernel50KBenchmarkTests` 注释）。Web 脚本把场景跑一遍并报告 headless `runScenario` 的 tick 统计；两端数字写入上表路径，更新文档时替换本段表格中的示例值。
