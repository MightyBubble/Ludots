# CrowdSimulation 演示场景

七个可玩的演示，都在种子 1337 的 S1 地图上，单位和建筑模板用 `CrowdSimulationDemoContentMod`。每个场景是一个纯数据 Mod，只写自己的指令脚本、标题和镜头；框选、右键下令、HUD、玩家视图都来自 S4 部署演示。进图后脚本自己往下跑，玩家随时可以框选单位另下命令。

## 场景

| 预设 | 场景 Mod | 演示什么 |
|---|---|---|
| `crowd_simulation_showcase_march_raylib` | `CrowdSimulationMarchShowcaseMod` | 120 人方块在图中心集结（约 441 tick），tick 450 向东行军，tick 480 改令转向约 60° |
| `crowd_simulation_showcase_reversal_raylib` | `CrowdSimulationReversalShowcaseMod` | 方块向东出发，tick 3 掉头往西，tick 46 同向再令一次 |
| `crowd_simulation_showcase_repeat_click_raylib` | `CrowdSimulationRepeatClickShowcaseMod` | 方块集结后，从 tick 441 起每 10 tick 对同一目标再点一次，共 8 次 |
| `crowd_simulation_showcase_fog_explore_raylib` | `CrowdSimulationFogExploreShowcaseMod` | 视野外的敌方小队、敌方目标和南边的新建筑都不画；tick 80 有 5 个敌人出现在本方旁边；tick 110 本方向南探索，约 tick 2689 看见建筑 |
| `crowd_simulation_showcase_fog_reroute_raylib` | `CrowdSimulationFogRerouteShowcaseMod` | 60 人从西向东横穿地图，tick 10、12 在迷雾里放两栋建筑，第一栋压在领队路线上；约 tick 2184 看见后改道 |
| `crowd_simulation_showcase_fog_shared_vision_raylib` | `CrowdSimulationFogAllyVisionShowcaseMod` | 玩家 1 原地不动，盟友玩家 3 去看东边的建筑（约 tick 185 看见）、再往西路过敌人（约 tick 609）；tick 800 拆掉建筑，玩家 1 这边留残影，盟友回去后约 tick 1281 残影消失 |
| `crowd_simulation_showcase_stress_50k_raylib` | `CrowdSimulationStress50kShowcaseMod` | 和 50k 门禁同一场景：开局 5 万步兵，tick 2 四方都开往 (1200000, 800000)；遥测面板默认打开 |

迷雾场景的玩家视图按玩家 1 的视野组隐藏敌方，地面贴迷雾。5 万单位场景不隐藏，四方都画出来。

## 启动

```bash
dotnet build mods/LudotsCoreMod -c Release
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release -- launch preset:crowd_simulation_showcase_reversal_raylib
```

换成表里的其它预设名即可。启动器界面里按标题“CrowdSimulation 演示 · …”找。

常用键沿用 S4：左键框选，右键下令，P 暂停，PgUp / PgDn 倍速，F10 开关遥测面板（模拟耗时和渲染耗时分两行），0 开关迷雾隐藏。完整键位在 HUD 第一行。

## 组成

- `CrowdSimulationShowcaseStageMod`：所有场景共用的舞台。依赖 S4 部署演示，把开局部署人数设为 0，清空 S4 的演示脚本，关掉自动回放，玩家视图按迷雾隐藏敌方。
- 场景 Mod：`assets/CrowdSimulationDebug.json` 写 `session.script` 和标题，`assets/Camera/virtual_cameras.json` 按 id 覆盖 `CrowdSimulation.Camera.S4Deploy` 的镜头位置，`assets/game.json` 指向 `crowd_simulation_s1337`。5 万单位场景另外打开遥测面板、关掉迷雾隐藏。
- 脚本数组整段替换，不追加，所以行军、折返、连点三个场景各自写一遍 tick 0 的 `spawnAt`。5 万单位场景的脚本与 `CrowdSimulationS4DeployMod/assets/CrowdSimulation/regress/stress-50k.json` 相同，验收测试逐条比对两边。

## 验收

`src/Tests/CrowdSimulationTests/Showcases/` 下的测试按启动器同样的 Mod 链起真实引擎、进图，让会话宿主自己跑脚本，在脚本指令的 tick 上核对对应 UAT 的不变量和阈值：

- `FormationShowcaseTests`：行进 UAT（连点逆向单位-tick ≤ 3%、人均翻转 ≤ 2 次；转向逆向 ≤ 2%）和折返 UAT（纵深 ≥ 4 格时掉头，单位相对位移 ≤ 纵深/2 + 1 格，mirror 继承）。场景坐标按 UAT 的格数在本会话导航上重算后与脚本比对。
- `FogShowcaseTests`：隐藏 UAT、迷雾改道 UAT、UAT-FOG-01/02。共享视野场景每 tick 跑一次仿真校验。
- `Stress50kShowcaseTests`：合并后的脚本等于门禁脚本，推过 40 tick 预热后单位数、迷雾刷新和遥测读数正常。不计时，不替代门禁。

```bash
dotnet test src/Tests/CrowdSimulationTests -c Release --filter "FullyQualifiedName~CrowdSimulationTests.Showcases"
```

真实窗口截图在 `artifacts/acceptance/crowdsimulation-showcases/`。
