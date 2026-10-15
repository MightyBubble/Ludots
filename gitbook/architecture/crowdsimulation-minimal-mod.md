# CrowdSimulation 最小样板：从空目录做一个群体地图 Mod

这页按实际执行顺序，记下 `CrowdSimulationMinimalMod` 是怎么从一个空目录做出来的：一种单位、一个表现、一张 256×256 导航格的小图，地形只有陆地和水面两种，中间一道方块墙。两名玩家各 200 个单位绕墙走到对岸。新建群体地图时照这页抄；每一步用到的文件和命令都列出来了，没有手写二进制，也没有整份复制别的 Mod。

规则本身（配置键、合并规则、烘焙参数）以 [CrowdSimulation 与 NavSurface](crowdsimulation-and-navsurface.md) 为准，这页只写怎么用。

## 做出来的东西

- Mod：`mods/showcases/crowd_simulation/CrowdSimulationMinimalMod`，纯数据，没有工程文件。
- 地图 `crowd_minimal`：边长 1,600,000 cm。标准参数的 `profiles.cellSizeCm` 是 6250，所以导航格是 256×256。
- 地形：全图 `land`，墙南端外侧一块 `water`（列 116–140、行 96–111）。
- 墙：31 个一格大小的方块阻挡物，列 128，行 112–142。南端贴着水，只能从北端绕。
- 单位：`crowd_minimal.scout`，移动类型 `foot`，半径 150 cm。`foot` 不进 `deep`，所以不下水。
- 脚本：tick 0 两名玩家各在墙西侧生成 200 个，tick 1 下令走到墙东侧。

## 前置

所有命令都在仓库根目录跑。`Ludots.Tool` 装载 Mod 时要找 `LudotsCoreMod` 的程序集，没先构建会报 `Mod 'LudotsCoreMod' declares main assembly but the DLL was not found`。

```bash
dotnet build mods/LudotsCoreMod/LudotsCoreMod.csproj -c Release
dotnet build src/Tools/Ludots.Tool/Ludots.Tool.csproj -c Release
T="dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -c Release --no-build --"
```

## 第 1 步：mod.json 和入口

`mod.json` 只依赖两个 Mod：能力 Mod `CrowdSimulationMod` 和标准参数 `CrowdSimulationStandardMod`，都写 `^1.0.0`。`LudotsCoreMod` 由它们间接带进来，不用再写。

| 文件 | 写什么 |
| --- | --- |
| `mod.json` | `name`、`version`、两条依赖 |
| `assets/game.json` | `startupMapId: crowd_minimal`；`startupLocalSeats` 把 `seat.0` 绑到玩家 1 |
| `assets/Engine/clock.json` | `{ "FixedHz": 30 }`。核心默认 20，群体地图的时钟由地图 Mod 自己写 |
| `assets/Camera/virtual_cameras.json` | 地图 `DefaultCamera` 引用的虚拟相机 `CrowdSimulationMinimal.Camera` |

## 第 2 步：导航配置

`assets/Navigation/navmesh.json` 只写这张图的 `maps.crowd_minimal`：

- `solver: crowd-simulation`。
- `logicTerrainAsset: terrain/crowd_minimal.map_data.bin`，下一步的工具和烘焙都读它。
- `terrainAreas`：`land → Default`，`water → deep`。数组下标就是地形编号，`land` 是 0，`water` 是 1。
- `navAreas`：除了 `Default`、`deep`，还要列 `road`、`deck`、`flooded`、`burning`。标准参数里的结构模板会动态铺这四种区域，少一个就进图失败。

`assets/Navigation/agent_profiles.json` 加一个体型 `crowd_minimal_scout`：`radiusCm` 150，`navProfileId` 是 `foot`。移动类型 `foot` 和它的 `areaCosts` 已经在标准参数的 `pathing.json` 里，不用重写。

## 第 3 步：地编网格和高度图

新图没有现成的逻辑地形，也没有 `.height`。这两个文件用工具生成：

```bash
$T nav init-crowd-terrain --mapId crowd_minimal --modId CrowdSimulationMinimalMod --fill land --rect water:116,96,140,111
$T nav init-crowd-heightmap --mapId crowd_minimal --modId CrowdSimulationMinimalMod --heightCm 100
```

两条命令都先读地图 json、`navmesh.json` 和群体配置，算出板边长、导航格数和格边长；`--mapId` 和 `--modId` 对不上就报错。

- `init-crowd-terrain` 写 `logicTerrainAsset` 指向的 React 网格。每格按导航格大小，`AreaId` 是 `terrainAreas` 下标。`--fill` 先铺满一种地形；`--rect 地形:minCol,minRow,maxCol,maxRow` 可以写多次，按顺序覆盖，边界包含两端。行号按世界 Y 从小到大数，行 0 是最南一行。越界、颠倒、未知地形 id 都会报错。
- `init-crowd-heightmap` 写 `ContinuousHeightmap.Asset` 指向的 `.height`：uint16 全 0，比例是 `(heightCm, 1, 1)`，所以整张图平在 `heightCm` 厘米。
- 文件已存在时两条命令都拒绝，要覆盖加 `--overwrite`。

之后想改地形，在 React 编辑器里打开这张图，用 Area 工具画；编辑器桥要求地编资产已经存在，所以新图必须先跑这一步。

## 第 4 步：地图 json

`assets/Maps/crowd_minimal.json`：

- `Tags: ["crowd_simulation"]`。
- 一块 `Grid` 板，`WidthCm` / `HeightCm` 1,600,000，`Grid.CellSizeCm` 100。板不设 `DataFile`。
- `ContinuousHeightmap.Asset: assets/terrain/crowd_minimal.height`。运行时把 `.height` 换成 `.navsurface` 去读地表，所以这个文件名也决定了下一步的输出路径。`RenderProfile.DisableDistanceFog: true` 让整张小图在默认相机下不被远雾盖住。
- `DefaultCamera` 对着墙：目标 (800000, 770000)，`Yaw` 0，`Pitch` 60，距离 400000。
- `Teams` 两个队伍互为敌对，`Players` 两名玩家。
- `Entities`：31 个 `crowd_minimal.wall_block`，`InstanceId` 是 `wall_112` 到 `wall_142`。位置是格心：X = 803125（列 128），Y = (行 + 0.5) × 6250。这 31 条是用一段脚本按公式生成后贴进来的，手写也一样。

墙块模板写在 `assets/Entities/templates.json`：`ManifestationObstacleIntent2D` 的 `shape` 是 `Box`，`sinkNavigationObstacle` 为 true，`halfWidthCm` = `halfHeightCm` = 3125，正好一个导航格。阻挡物只能是正方形，这是 `CrowdSimulationAuthoringContract` 的要求。

## 第 5 步：烘焙 .navsurface

```bash
$T nav bake-crowd-navsurface --mapId crowd_minimal --modId CrowdSimulationMinimalMod --out mods/showcases/crowd_simulation/CrowdSimulationMinimalMod/assets/terrain/crowd_minimal.navsurface
```

输出 `256x256, types=2, jumps=0`。这张图没写 `jumpLinkBake`，所以没有跳跃候选。改了地编网格或 `.height` 就重跑这条命令（已有文件要加 `--overwrite`），再提交。测试会拿入库文件和现烤结果逐字节比较。

## 第 6 步：单位和表现

单位模板 `crowd_minimal.scout` 写在 `assets/Entities/templates.json`，`CrowdSimulationAgent` 六个字段都必填：`profileId: crowd_minimal_scout`、`agentTypeId: foot`、`speedCmPerSecond: 1400`、`personalRadiusCm: 900`、`pushPriority: 2`、`special: false`。另有 `crowd_minimal.team` 和 `crowd_minimal.player` 两个只有 `Name` 的模板，给地图的队伍和玩家用。

同一个文件里还要删掉 `CrowdSimulationMod` 自带的 20 个演示单位模板（`crowd_simulation.unit.{infantry,navy,mountaineer,amphibious,jumper}.r{100,150,200,400}`），每条写 `{ "id": "...", "__delete": true }`。原因：群体运行时绑定所有带 `CrowdSimulationAgent` 的模板，并要求“每种移动类型 × 每个半径级”都有模板。不删的话，演示单位会把 `hull` 等移动类型带进来，而这张图只有一个 150 cm 的单位，进图报“移动类型 hull 缺半径级 150 的模板”。删完以后只剩 `foot × r150` 一格，所以脚本里写 `unitType 0`、`rIdx 0`。

表现写在 `assets/Presentation/presenters.json`：

- 同样用 `__delete` 删掉 `CrowdSimulationMod` 的 `crowd_simulation.units.bootstrap`，它按演示模板 id 绑定表现。
- 新的 `crowd_minimal.units.bootstrap`：`EntitySpawned` / `EntityDestroyed` 按模板 `crowd_minimal.scout` 建删表现。
- 表现定义 `crowd_minimal.scout`：网格 `crowd_simulation.unit.square`，缩放 [12, 8, 12]，偏移 [0, 4, 0]；颜色参数 `crowd_minimal.scout.color` 接 `entityColorVector`，单位按 `deploy.bases[].color` 上色；`Grounding` 用 `SnapToGround`。

## 第 7 步：群体配置和脚本

`assets/CrowdSimulationConfig.json` 只写和标准参数不同的键，按 DeepObject 合并：

- `world.seed: 1`。
- `deploy.initialUnits: 0`。标准参数是 12000，不改就会先撒一万多个单位。
- `deploy.bases`：玩家 1 在 (715625, 828125)，颜色 `#4DB2FF`；玩家 2 在 (715625, 765625)，颜色 `#FFC53D`。
- `structures.anchorTerrain: ["land"]`、`structures.spanTerrain: ["water"]`。标准参数的锚点地形是 `["shore", "land"]`，这张图没有 `shore`，不改就进图失败。

`assets/CrowdSimulationDebug.json` 放开场脚本 `session.script`：tick 0 两条 `spawnAt`（各 200 个，`unitType 0`，`rIdx 0`），tick 1 两条 `order`，目标是 (890625, 828125) 和 (890625, 765625)。这份文件要在本 Mod 的 `assets/config_catalog.json` 里按 `DeepObject` 登记。

`assets/Presentation/crowd_terrain_type_overlay.json` 写 `{ "enabled": true }`，进图就显示地形类型叠层；按 `[` 切换。地形颜色和迷雾贴花颜色用标准参数里的 `Presentation/terrain_type_colors.json` 和 `Presentation/crowd_fog_decal.json`，这里不用写。

## 第 8 步：启动、展厅登记和测试

启动项：

- `launcher.config.json` 加绑定 `crowd_simulation_minimal` → `mods/showcases/crowd_simulation/CrowdSimulationMinimalMod`。
- `launcher.presets.json` 加预设 `crowd_simulation_minimal_raylib`。
- `showcase.registry.json` 加条目 `crowd_simulation_minimal`，验收测试和截图目录都写在里面。

在 Raylib 里跑（Linux 没有 pwsh 时直接用 CLI）：

```bash
dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release --no-build -- launch preset:crowd_simulation_minimal_raylib
```

测试在 `src/Tests/CrowdSimulationTests/Authoring/`：

```bash
dotnet test src/Tests/CrowdSimulationTests/CrowdSimulationTests.csproj -c Release --filter "FullyQualifiedName~CrowdSimulationTests.Authoring"
```

- `CrowdSimulationMinimalModTests.StoredNavSurface_IsFreshLudotsBake`：入库的 `.navsurface` 和现烤结果逐字节一致。
- `CrowdSimulationMinimalModTests.TwoPlayers_GoAroundTheWall_StayOffBlockedAndWater_AndArriveSpread`：按真实的 Mod 装载和进图路径跑 2000 tick。每个 tick 检查没有单位站在阻挡格或水格上；到了墙东侧的单位，走过的行必须越过墙的南端或北端；至少 95% 的单位到达且在墙东侧；每名玩家的到达点最近两两距离不小于两个半径，外接半径不小于 150·√N 厘米，确认没有挤成一点。
- `CrowdAuthoringStartTests`：`--rect` 的覆盖顺序和报错，平高度图解码回指定高度。

验收截图在 `artifacts/acceptance/crowdsimulation-minimal/`：`crowd_minimal_around_wall.png` 是单位绕墙北端，`crowd_minimal_fog_aligned.png` 是关掉地形叠层后玩家 1 的迷雾。

## 完整文件清单

| 文件 | 来源 |
| --- | --- |
| `mod.json` | 手写 |
| `assets/game.json` | 手写 |
| `assets/Engine/clock.json` | 手写 |
| `assets/Camera/virtual_cameras.json` | 手写 |
| `assets/config_catalog.json` | 手写 |
| `assets/Navigation/navmesh.json` | 手写 |
| `assets/Navigation/agent_profiles.json` | 手写 |
| `assets/Entities/templates.json` | 手写 |
| `assets/Presentation/presenters.json` | 手写 |
| `assets/Presentation/crowd_terrain_type_overlay.json` | 手写 |
| `assets/CrowdSimulationConfig.json` | 手写 |
| `assets/CrowdSimulationDebug.json` | 手写 |
| `assets/Maps/crowd_minimal.json` | 手写，墙块条目脚本生成 |
| `assets/terrain/crowd_minimal.map_data.bin` | `nav init-crowd-terrain` |
| `assets/terrain/crowd_minimal.height` | `nav init-crowd-heightmap` |
| `assets/terrain/crowd_minimal.navsurface` | `nav bake-crowd-navsurface` |

## 做的过程中撞到的问题

下面几条是照现有文档做新图时缺的步骤或卡住的地方。前两条和第七、九条已经在代码里修了，其余的还在。

1. 没有工具生成群体地图的逻辑地形，编辑器桥打开不存在的地编资产会拒绝。新增 `nav init-crowd-terrain`；烘焙时地编资产缺失，报错里会提示先跑这条命令。
2. 没有工具生成新图的 `.height`。新增 `nav init-crowd-heightmap`。
3. 标准参数的 `structures.anchorTerrain` 是 `["shore", "land"]`，只有陆地和水的图必须自己覆盖。
4. `CrowdSimulationMod` 的 20 个演示单位模板和演示表现总会被装进来，而运行时要求“移动类型 × 半径级”填满。只想要一种单位，现在只能用 `__delete` 删掉它们。批量 `spawn` 也按满表取模板。把演示单位挪出能力 Mod 属于基础设施改动，这次没做。
5. 标准参数的 `deploy.initialUnits` 是 12000，要覆盖成 0；`navAreas` 必须列上结构模板会铺的 `road`、`deck`、`flooded`、`burning`。
6. `mod init` 生成的是带 C# 工程的 Mod，地图格式也旧，对纯数据的群体地图没用。这次没改。
7. 迷雾贴花的四色原来只在调试配置的 `presentation.fogDecal` 里，没写调试配置的群体地图一出单位，Raylib 就因为缺调色板报错。现在四色在 `Presentation/crowd_fog_decal.json`，标准参数给默认值，群体会话每次激活都发布 `FogDecalPalette`。
8. 有一个单位会卡在墙南端和水面之间的凹角格 (127, 112)。这一格对导航是可走的，问题在避让和转向，没修。验收按 2000 tick、95% 到达写。
9. Raylib 里地形类型叠层、迷雾和流场贴花上下颠倒：水画在了墙北边，迷雾的可见圈也在单位的镜像位置。原因是地形着色器把所有贴花纹理都按图片文件的行序翻转（首行是北），而这三种纹理是引擎按格行写的，首行是最小 Y。现在渲染器告诉着色器当前纹理的行序，只有图片文件才翻转。
