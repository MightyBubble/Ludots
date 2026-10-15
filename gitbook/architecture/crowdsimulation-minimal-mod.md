# CrowdSimulation 最小样板：从空目录做一个群体地图 Mod

这页按实际执行顺序，记下 `CrowdSimulationMinimalMod` 是怎么从一个空目录做出来的：一种单位、一个表现、一张 256×256 导航格的小图，地形只有陆地和水面两种，中间一道方块墙。两名玩家各 200 个单位绕墙走到对岸。新建群体地图时照这页做：先用一条命令生成骨架，再按这张图改几处；每一步用到的文件和命令都列出来了，没有手写二进制，也没有复制别的 Mod。

规则本身（配置键、合并规则、烘焙参数）以 [CrowdSimulation 与 NavSurface](crowdsimulation-and-navsurface.md) 为准，这页只写怎么用。

## 做出来的东西

- Mod：`mods/showcases/crowd_simulation/CrowdSimulationMinimalMod`，纯数据，没有工程文件。只依赖能力 Mod `CrowdSimulationMod` 和标准参数 `CrowdSimulationStandardMod`，不依赖演示内容 Mod。
- 地图 `crowd_minimal`：边长 1,600,000 cm。`navmesh.json` 里这张图的 `navCellSizeCm` 是 6250，所以导航格是 256×256。
- 地形：全图 `land`，墙南端外侧一块 `water`（列 116–140、行 96–111）。
- 墙：31 个一格大小的方块阻挡物，列 128，行 112–142。南端贴着水，只能从北端绕。
- 单位：`crowd_minimal.unit`，移动类型 `foot`，半径 150 cm。`foot` 不进 `deep`，所以不下水。
- 脚本：tick 0 两名玩家各在墙西侧生成 200 个，tick 1 下令走到墙东侧。

## 前置

所有命令都在仓库根目录跑。`Ludots.Tool` 装载 Mod 时要找 `LudotsCoreMod` 的程序集，没先构建会报 `Mod 'LudotsCoreMod' declares main assembly but the DLL was not found`。

```bash
dotnet build mods/LudotsCoreMod/LudotsCoreMod.csproj -c Release
dotnet build src/Tools/Ludots.Tool/Ludots.Tool.csproj -c Release
T="dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -c Release --no-build --"
```

## 第 1 步：生成骨架

```bash
$T mod init --id CrowdSimulationMinimalMod --template crowd-data --mapId crowd_minimal --dir mods/showcases/crowd_simulation
```

`--template crowd-data` 只写数据文件，不生成工程文件和入口类；`--mapId` 必填，目录已存在就拒绝。生成的文件：

| 文件 | 内容 |
| --- | --- |
| `mod.json` | 依赖 `CrowdSimulationMod`、`CrowdSimulationStandardMod`，都写 `^1.0.0`。`LudotsCoreMod` 由它们间接带进来 |
| `assets/game.json` | `startupMapId`；`startupLocalSeats` 把 `seat.0` 绑到玩家 1 |
| `assets/Engine/clock.json` | `{ "FixedHz": 30 }`。核心默认 20，群体地图的时钟由地图 Mod 自己写 |
| `assets/Camera/virtual_cameras.json` | 虚拟相机 `<ModId>.Camera`，地图 `DefaultCamera` 引用它 |
| `assets/Navigation/navmesh.json` | `maps.<mapId>`：`solver`、`logicTerrainAsset`、`navCellSizeCm`、`terrainTypes`（见第 2 步） |
| `assets/Navigation/agent_profiles.json` | 体型 `<mapId>_unit`：`radiusCm` 150，`navProfileId` 是 `foot` |
| `assets/Maps/<mapId>.json` | 一块 1,600,000 cm 的 `Grid` 板、高度图路径、相机、两个敌对队伍、两名玩家（见第 4 步） |
| `assets/Entities/templates.json` | 队伍、玩家、单位三个模板（见第 6 步） |
| `assets/Presentation/presenters.json`、`mesh_assets.json` | 单位的表现和网格（见第 6 步） |
| `assets/CrowdSimulationConfig.json` | 这张图的群体配置（见第 7 步） |
| `assets/CrowdSimulationDebug.json`、`config_catalog.json` | 开场脚本，以及把它按 `DeepObject` 登记的目录 |
| `assets/Presentation/crowd_terrain_type_overlay.json` | `{ "enabled": true }`，进图就显示地形类型叠层，按 `[` 切换 |

骨架本身就能进图：跑完第 3、5 步的三条 nav 命令，两名玩家各 100 个单位从 (600000, 800000) 和 (1000000, 800000) 走向对方。`src/Tests/CrowdSimulationTests/Authoring/CrowdModScaffoldTests.cs` 按这个流程跑一遍。下面各步写的是这张图在骨架上改了什么。

`mod.json` 改了 `description`，`tags` 加上 `showcase`、`navsurface`、`minimal`。

## 第 2 步：导航配置

`assets/Navigation/navmesh.json` 不用改：

- `solver: crowd-simulation`。
- `logicTerrainAsset: terrain/crowd_minimal.map_data.bin`，下一步的工具和烘焙都读它。
- `navCellSizeCm: 6250`，导航格边长。
- `terrainTypes`：`land`、`water`。数组下标就是地形编号，`land` 是 0，`water` 是 1。地形到区域的对应在标准参数的 `CrowdSimulation/traversal.json`：`land → open`，`water → deep`。标准参数里只有 `building` 一个结构模板，它只挡格、不铺区域，所以不用加区域。

移动类型 `foot` 那一行代价和可走区域已经在标准参数的 `CrowdSimulation/traversal.json` 里，不用重写。

## 第 3 步：地编网格和高度图

新图没有现成的逻辑地形，也没有 `.height`。这两个文件用工具生成：

```bash
$T nav init-crowd-terrain --mapId crowd_minimal --modId CrowdSimulationMinimalMod --fill land --rect water:116,96,140,111
$T nav init-crowd-heightmap --mapId crowd_minimal --modId CrowdSimulationMinimalMod --heightCm 100
```

两条命令都先读地图 json、`navmesh.json` 和群体配置，算出板边长、导航格数和格边长；`--mapId` 和 `--modId` 对不上就报错。

- `init-crowd-terrain` 写 `logicTerrainAsset` 指向的 React 网格。每格按导航格大小，`AreaId` 是 `terrainTypes` 下标。`--fill` 先铺满一种地形；`--rect 地形:minCol,minRow,maxCol,maxRow` 可以写多次，按顺序覆盖，边界包含两端。行号按世界 Y 从小到大数，行 0 是最南一行。越界、颠倒、未知地形 id 都会报错。
- `init-crowd-heightmap` 写 `ContinuousHeightmap.Asset` 指向的 `.height`：uint16 全 0，比例是 `(heightCm, 1, 1)`，所以整张图平在 `heightCm` 厘米。
- 文件已存在时两条命令都拒绝，要覆盖加 `--overwrite`。

之后想改地形，在 React 编辑器里打开这张图，用 Area 工具画；编辑器桥要求地编资产已经存在，所以新图必须先跑这一步。

## 第 4 步：地图 json

骨架里的 `assets/Maps/crowd_minimal.json` 已经有：

- `Tags: ["crowd_simulation"]`。
- 一块 `Grid` 板，`WidthCm` / `HeightCm` 1,600,000，`Grid.CellSizeCm` 100。板不设 `DataFile`。
- `ContinuousHeightmap.Asset: assets/terrain/crowd_minimal.height`。运行时把 `.height` 换成 `.navsurface` 去读地表，所以这个文件名也决定了第 5 步的输出路径。`RenderProfile.DisableDistanceFog: true` 让整张小图在默认相机下不被远雾盖住。
- `Teams` 两个队伍互为敌对，`Players` 两名玩家，以及它们的代表实体。

这张图改了两处：

- `DefaultCamera` 的目标改成 (800000, 770000)，对着墙。
- `Entities` 加 31 个 `crowd_minimal.wall_block`，`InstanceId` 是 `wall_112` 到 `wall_142`。位置是格心：X = 803125（列 128），Y = (行 + 0.5) × 6250。这 31 条是用一段脚本按公式生成后贴进来的，手写也一样。

墙块模板加在 `assets/Entities/templates.json`：`ManifestationObstacleIntent2D` 的 `shape` 是 `Box`，`sinkNavigationObstacle` 为 true，`halfWidthCm` = `halfHeightCm` = 3125，正好一个导航格。阻挡物只能是正方形，这是 `CrowdSimulationAuthoringContract` 的要求。运行时用标准参数里的结构模板 `building` 管这些阻挡物。

## 第 5 步：烘焙 .navsurface

```bash
$T nav bake-crowd-navsurface --mapId crowd_minimal --modId CrowdSimulationMinimalMod --out mods/showcases/crowd_simulation/CrowdSimulationMinimalMod/assets/terrain/crowd_minimal.navsurface
```

输出 `256x256, types=2, linkKinds=[], linkCandidates=0, linkBakeDigest=0x8227BA97`。这张图没写 `linkCandidateBake`，所以链接种类表是空的，没有候选，摘要只含导航格边长。改了地编网格或 `.height` 就重跑这条命令（已有文件要加 `--overwrite`），再提交。测试会拿入库文件和现烤结果逐字节比较。

## 第 6 步：单位和表现

骨架写好的单位不用改。

单位模板 `crowd_minimal.unit` 在 `assets/Entities/templates.json`，`CrowdSimulationAgent` 六个字段都必填：`profileId: crowd_minimal_unit`、`agentTypeId: foot`、`speedCmPerSecond: 1400`、`personalRadiusCm: 900`、`pushPriority: 2`、`special: false`。另有 `crowd_minimal.team` 和 `crowd_minimal.player` 两个只有 `Name` 的模板，给地图的队伍和玩家用。

群体运行时绑定所有带 `CrowdSimulationAgent` 的模板，并要求“每种移动类型 × 每个半径级”都有模板。这个 Mod 只装了能力 Mod 和标准参数，两者都不带单位模板，所以绑定集只有 `foot × r150` 一格，脚本里写 `unitType 0`、`rIdx 0`。S1、S3、S4 用的 20 个演示单位在 `CrowdSimulationDemoContentMod`，不依赖它就不会装进来。

表现在 `assets/Presentation/presenters.json` 和 `mesh_assets.json`：

- `crowd_minimal.units.bootstrap`：`EntitySpawned` / `EntityDestroyed` 按模板 `crowd_minimal.unit` 建删表现。
- 表现定义 `crowd_minimal.unit`：网格 `crowd_minimal.unit.body`（`mesh_assets.json` 里的立方体原语），缩放 [12, 8, 12]，偏移 [0, 4, 0]；颜色参数 `crowd_minimal.unit.color` 接 `entityColorVector`，单位按 `deploy.bases[].color` 上色；`Grounding` 用 `SnapToGround`。
- 选中圈由能力 Mod 提供，不用写。

## 第 7 步：群体配置和脚本

`assets/CrowdSimulationConfig.json` 只写这张图的事实，按 DeepObject 叠在标准参数上：

- `world.seed: 1`。
- `deploy.initialUnits: 0`。开局不撒单位，单位由脚本生成。
- `deploy.bases`：这张图改成玩家 1 在 (715625, 828125)，颜色 `#4DB2FF`；玩家 2 在 (715625, 765625)，颜色 `#FFC53D`。
`initialUnits`、`bases` 没有标准值，地图不写就加载失败。桥能落在哪类区域写在结构模板的 `placement`（`endpointAreas`、`interiorAreas`），这张图不放桥，不用写。

`assets/CrowdSimulationDebug.json` 的开场脚本 `session.script` 改成：tick 0 两条 `spawnAt`（各 200 个，`unitType 0`，`rIdx 0`），tick 1 两条 `order`，目标是 (890625, 828125) 和 (890625, 765625)。

地形颜色和迷雾贴花颜色用标准参数里的 `Presentation/terrain_type_colors.json` 和 `Presentation/crowd_fog_decal.json`，这里不用写。

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
- `CrowdSimulationMinimalModTests.TwoPlayers_GoAroundTheWall_StayOffBlockedAndWater_AndArriveSpread`：按真实的 Mod 装载和进图路径跑 2000 tick。每个 tick 检查没有单位站在阻挡格或水格上；到了墙东侧的单位，走过的行必须越过墙的南端或北端；至少 95% 的单位到达且在墙东侧；每名玩家的到达点最近两两距离不小于两个半径，外接半径不小于 150·√N 厘米，确认没有挤成一点。实测 399/400 到达，没到的那个卡在凹角里，原因见下面第 8 条。
- `CrowdModScaffoldTests`：`mod init --template crowd-data` 的骨架跑完三条 nav 命令后能进图，两名玩家的单位朝对方走；目录已存在、缺 `--mapId` 时拒绝。
- `CrowdAuthoringStartTests`：`--rect` 的覆盖顺序和报错，平高度图解码回指定高度。

验收截图在 `artifacts/acceptance/crowdsimulation-minimal/`：`crowd_minimal_around_wall.png` 是单位绕墙北端，`crowd_minimal_fog_aligned.png` 是关掉地形叠层后玩家 1 的迷雾。

## 完整文件清单

| 文件 | 来源 |
| --- | --- |
| `mod.json` | `mod init`，改了描述和标签 |
| `assets/game.json` | `mod init` |
| `assets/Engine/clock.json` | `mod init` |
| `assets/Camera/virtual_cameras.json` | `mod init` |
| `assets/config_catalog.json` | `mod init` |
| `assets/Navigation/navmesh.json` | `mod init` |
| `assets/Navigation/agent_profiles.json` | `mod init` |
| `assets/Entities/templates.json` | `mod init`，加了墙块模板 |
| `assets/Presentation/presenters.json` | `mod init` |
| `assets/Presentation/mesh_assets.json` | `mod init` |
| `assets/Presentation/crowd_terrain_type_overlay.json` | `mod init` |
| `assets/CrowdSimulationConfig.json` | `mod init`，改了出生点 |
| `assets/CrowdSimulationDebug.json` | `mod init`，改了脚本 |
| `assets/Maps/crowd_minimal.json` | `mod init`，改了相机目标，墙块条目脚本生成 |
| `assets/terrain/crowd_minimal.map_data.bin` | `nav init-crowd-terrain` |
| `assets/terrain/crowd_minimal.height` | `nav init-crowd-heightmap` |
| `assets/terrain/crowd_minimal.navsurface` | `nav bake-crowd-navsurface` |

## 做的过程中撞到的问题

下面几条是照现有文档做新图时缺的步骤或卡住的地方。除第 8 条外都已经在代码或数据里改了。

1. 没有工具生成群体地图的逻辑地形，编辑器桥打开不存在的地编资产会拒绝。新增 `nav init-crowd-terrain`；烘焙时地编资产缺失，报错里会提示先跑这条命令。
2. 没有工具生成新图的 `.height`。新增 `nav init-crowd-heightmap`。
3. 标准参数原来写死了 S1 的桥锚点地形 `["shore", "land"]`，只有陆地和水的图必须自己覆盖。现在锚点地形和跨越地形是地图事实，S1 系列的那份在演示内容 Mod，别的地图自己写。
4. `CrowdSimulationMod` 原来带着 20 个演示单位模板、演示表现和网格，运行时又要求“移动类型 × 半径级”填满，只想要一种单位就得用 `__delete` 一条条删掉。现在演示单位挪到 `CrowdSimulationDemoContentMod`，能力 Mod 只剩会话模板和选中圈，这个 Mod 里不再有删除条目。
5. 标准参数原来写着 `deploy.initialUnits: 12000` 和道路、桥、洪水、火灾结构模板，每张新图都要覆盖人数，地图还要列上这四种结构区域。现在开局人数是地图事实，这四个模板和它们的区域都跟着演示内容 Mod 走，地图不再列区域。
6. `mod init` 原来只会生成带 C# 工程的 Mod，地图格式也旧，对纯数据的群体地图没用。现在加了 `--template crowd-data`，第 1 步就是这条命令。
7. 迷雾贴花的四色原来只在调试配置的 `presentation.fogDecal` 里，没写调试配置的群体地图一出单位，Raylib 就因为缺调色板报错。现在四色在 `Presentation/crowd_fog_decal.json`，标准参数给默认值，群体会话每次激活都发布 `FogDecalPalette`。
8. 有一个单位会卡在墙南端和水面之间的凹角格 (127, 112)。它先跟着阵型槽位顺着墙西侧往南走，换回流场后，北边人群的分离推力大过朝北的意图，把它推到角里。到了角里，`CrowdWalls.ResolveWalls` 先把它推出水格、再推出墙格，只交回墙的法线，马达只去掉朝墙的速度，朝水的速度留着；转向限速每步只把这个速度转一点，下一步又顶回水里，速度一直停在 (0, −670) cm/s。Web 参考的 `walls.js` / `motor.js` 是同一算法。改成每个接触都去速度，这个单位能走出来，但 S5 轨迹对拍超出带宽，所以没改，要和 Web 一起改。验收按 2000 tick、95% 到达写。
9. Raylib 里地形类型叠层、迷雾和流场贴花上下颠倒：水画在了墙北边，迷雾的可见圈也在单位的镜像位置。原因是地形着色器把所有贴花纹理都按图片文件的行序翻转（首行是北），而这三种纹理是引擎按格行写的，首行是最小 Y。现在渲染器告诉着色器当前纹理的行序，只有图片文件才翻转。
