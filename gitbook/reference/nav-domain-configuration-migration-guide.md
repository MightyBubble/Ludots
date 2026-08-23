# 导航域配置迁移指南

本页给要升级旧 Mod / showcase / 工具脚本的人用。目标是把旧导航配置迁到 Epic #281 后的新架构：尺度归 `SpatialScaleDefaults` / board，地形归 `LogicTerrainField`，障碍归 manifestation/shape/compound state，route 归 pathing policy，移动执行归 MassNavigationFlow。

## 迁移总原则

- 不做别名兼容：旧键、错大小写、缺 owner 的配置应 fail-fast。
- 不新增 fallback：缺数据时修配置或补正式管线，不在 feature 里临时造 loader。
- 不重复数据源：障碍、agent、地形、area id 只能有一个 SSOT。
- 不把生成物当配置：`.ntil` 是 bake output，不是作者手写输入。

## 快速迁移表

| 旧做法 | 新做法 | 说明 |
|---|---|---|
| `WidthInTiles` / `HeightInTiles` | `Boards[].WidthInMacroTiles` / `HeightInMacroTiles` | 单位是 256-cell MacroTile 数量；旧键出现即 fail-fast。 |
| 创建地图时手填 chunk 数 | 输入目标米数 + `GridCellSizeCm`，自动派生 MacroTiles / cells / Terrain/NavTiles | 编辑器 New / Add Board 已按这个模型工作。 |
| 代码里 inline `256` / `64` / `100` | `SpatialScaleDefaults.MacroTileCells` / `TerrainChunkCells` / `CellCm` 等命名常量 | `NavigationSpatialScaleMagicNumberContractTests` 会扫 board/bake/MassNavigationFlow 代码。 |
| 用 `NavTile` 命名地形块尺度 | `TerrainChunk` footprint | NavTile 是 bake 产物用途，不是新的尺度 owner。 |
| 私有 obstacle json / loader | `ManifestationObstacleIntent2D` + `ShapeDataStorage2D` + `CompoundObstacle2DState` | bake 与 MassNavigationFlow 执行消费同一障碍数据。 |
| `ObstacleGeometryProfile2D` | 不创建 | 主线不存在这个 owner。 |
| 视觉高度图直接当 navmesh 输入 | Board policy 选择 `continuous-heightmap`，与 `board-logic-terrain` 分类组合 | VHTM 直接提供连续几何；不生成投影后的逻辑高度中间层。 |
| 每条工具链单独写 bake 参数 | `NavBakeContext` + `NavBakeService` | CLI、Bridge、runtime incremental 共用同一对象。 |
| 假 navmesh 平面 / 自造寻路 debug | DotRecast Recast/Detour artifact + query result | debug view 必须能说明 tile/profile/layer/query engine。 |
| 旧二维导航移动执行 | MassNavigationFlow / MassNavigation execution sink | route 只产出路径/waypoints，执行统一进入 MassNavigationFlow。 |
| road showcase 私有 move runtime | 通用 move-plan seam + MassNavigationFlow sink | road/waterway 只保留 policy 和 graph 数据。 |

## MapConfig 迁移

旧配置常见问题：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthInTiles": 64,
      "HeightInTiles": 64,
      "GridCellSizeCm": 100
    }
  ]
}
```

迁移后：

```json
{
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthInMacroTiles": 64,
      "HeightInMacroTiles": 64,
      "GridCellSizeCm": 100,
      "HexEdgeLengthCm": 400,
      "ChunkSizeCells": 64,
      "NavigationEnabled": true,
      "DataFile": "default.bin"
    }
  ]
}
```

作者视角不要从 chunk 数倒推。先问地图要多大：

```text
targetWidthMeters = 1000
GridCellSizeCm    = 100
MacroTileCells    = 256

requestedCells      = ceil(1000m * 100 / 100cm) = 1000 cells
WidthInMacroTiles   = ceil(1000 / 256) = 4
allocatedWidthCells = 4 * 256 = 1024 cells
allocatedWidthM     = 1024m
Terrain/NavTiles    = 4 * (256 / 64) = 16
```

也就是说 `1000m` 会分配成 `1024m`，因为 MapConfig 必须落在整 MacroTile 边界。

## Agent / Pathing 迁移

Agent profile 迁移目标：

```json
{
  "id": "light",
  "radiusCm": 48,
  "heightCm": 180,
  "clearanceCm": 12,
  "draftCm": 0,
  "beamCm": 0,
  "mass": 1,
  "layer": 0
}
```

Pathing policy 只引用 profile：

```json
{
  "agentTypes": [
    {
      "id": "infantry",
      "profileId": "light",
      "selection": { "mode": "AutoCheapest" }
    },
    {
      "id": "caravan",
      "profileId": "heavy",
      "selection": { "mode": "PreferGraph" }
    }
  ]
}
```

迁移要求：

- 不在 MassNavigationFlow、navmesh、road 三处复制 radius / height / clearance。
- profile id 大小写严格；找不到 profile 应 fail-fast。
- area cost / blocked layer 是 agent 与 navmesh profile 的配置，不是 terrain cell 的隐式语义。

## 障碍迁移

旧式 blocker、碰撞体、nav obstacle 分散配置，迁到 entity/component：

```json
{
  "template": "stone_blocker",
  "overrides": {
    "WorldPositionCm": { "Value": { "X": 1200, "Y": 800 } },
    "ManifestationObstacleIntent2D": {
      "shape": "Box",
      "sinkPhysicsCollider": false,
      "sinkNavigationObstacle": true,
      "navRadiusCm": 300,
      "halfWidthCm": 300,
      "halfHeightCm": 200,
      "localOffsetXCm": 0,
      "localOffsetYCm": 0
    }
  }
}
```

Bake、runtime incremental、MassNavigationFlow avoidance 都应从 manifestation / shape / compound state 读取障碍。不要给 Recast 单独造一份障碍 json。

## 地形与 area 迁移

`LogicTerrainField` 是 nav bake 的地形 SSOT：

- Grid editor 的高度、水、blocked、area id 写入逻辑地形数据。
- Hex / VertexMap 通过 `VertexMapLogicTerrainField` 暴露同一接口。
- Nav bake 的 `continuous-heightmap` 直接采样 `IVisualHeightmap`；`LogicTerrainField` 继续提供 blocked/water/ramp/area/topology。通用 projection adapter 只用于明确要求量化逻辑地形的其他功能，不是 Nav bake 默认路径。
- `areaId` 是通行 cost / layer 的索引，不是颜色或材质名字。

迁移时需要确认：

- 高度档位不超过 `SpatialScaleDefaults.LogicTerrainMaxHeightLevel`。
- slope / max climb / agent height / clearance 来自 navmesh profile 与 agent profile。
- 脏 tile bake 只覆盖 dirty + neighbor，不清空已存在且未重烤的 tile。

## Bake / CLI / Bridge 迁移

估算：

```powershell
dotnet run --project .\src\Tools\Ludots.Tool\Ludots.Tool.csproj -- nav estimate-recast-react `
  --mapId nav_editor_grid `
  --modId LudotsCoreMod `
  --boardName default `
  --in map_data.bin `
  --dirty dirty_chunks.json `
  --includeNeighbors true `
  --heightScale 2 `
  --minUpDot 0.6 `
  --cliffThreshold 1 `
  --parallel true `
  --maxDegree 8 `
  --tileVersion 1
```

烘焙：

```powershell
dotnet run --project .\src\Tools\Ludots.Tool\Ludots.Tool.csproj -- nav bake-recast-react `
  --mapId nav_editor_grid `
  --modId LudotsCoreMod `
  --boardName default `
  --in map_data.bin `
  --dirty dirty_chunks.json `
  --includeNeighbors true `
  --heightScale 2 `
  --minUpDot 0.6 `
  --cliffThreshold 1 `
  --artifact true `
  --parallel true `
  --maxDegree 8 `
  --tileVersion 1
```

Bridge / Web editor 迁移要求：

- `Estimate` 显示真实预算、hash、operation、预计耗时。
- `Bake` 直接表示显式执行动作；不需要 large bake checkbox。
- Simulation 页签左键选起点、右键选终点，走 C# Bridge + DotRecast Detour 查询。
- Grid 空白 board 可以有默认 flat baseline navmesh，但它必须标明来源；正式 bake 后使用 Recast 输出。

## 执行迁移

旧执行：

```text
order -> retired 2D navigation target -> custom movement step
```

新执行：

```text
order -> pathing policy -> route / mesh / graph -> move plan -> MassNavigationFlow target -> MassNavigation/MassNavigationFlow execution
```

迁移检查：

- route 不直接写 `WorldPositionCm`。
- road/waterway/mesh 只决定 path / waypoint / cost。
- arrival、avoidance、动态障碍响应归 MassNavigationFlow 执行层。
- presentation debug 只读正式 runtime 状态，不写执行真相。

## 验证清单

每次迁移至少跑：

```powershell
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter NavigationSpatialScaleMagicNumberContractTests
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter NavBakeServiceContractTests
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter NavMeshConfigContractTests
dotnet build src/Tools/Ludots.Editor.Bridge/Ludots.Editor.Bridge.csproj --no-restore
cd src/Tools/Ludots.Editor.React
npm run build
```

人工 UAT：

1. Web editor 选择 mod / map / board，`Open` 后才允许编辑 canvas。
2. `New` / `Add Board` 只输入米数和尺度参数，确认派生 MacroTiles / cells / Terrain/NavTiles。
3. 画高度、blocked、area id、障碍，dirty chunk 出现在 minimap / nav 状态里。
4. `Estimate` -> `Bake` -> `Simulation`，确认 path source 是 Recast/Detour 或明确的 flat baseline。
5. 保存后重开同一 map/board，确认 MapConfig、terrain、entities、navigation config 都从 Bridge 持久化恢复。

## 常见失败

| 失败 | 原因 | 修复 |
|---|---|---|
| `WidthInTiles` 被拒绝 | 旧键不再兼容 | 改为 `WidthInMacroTiles` / `HeightInMacroTiles`。 |
| 编辑器看起来要求 chunk 数 | 表单未迁到 meters-first | 只保留目标米数输入，chunk / tile 作为派生预览。 |
| bake 后旧 tile 消失 | 写入时覆盖了 tile set，而不是按 key merge | dirty bake 必须 merge `.ntil` / payload。 |
| path 是假的直线/折线 | 没走 Detour query 或没加载 detour tile data | 重跑 Recast bake，确认 `detourBase64` / artifact 来源。 |
| Hex 自定义 edge 和 runtime 不一致 | 某些旧代码仍走静态 `HexCoordinates` 默认 | 把有 board context 的路径迁到 `HexMetrics` / board metrics。 |
| 大 bake 没进度 | UI 用确认框代替状态机 | 显示 estimate / progress / phase / error，不用 checkbox 充当进度。 |
