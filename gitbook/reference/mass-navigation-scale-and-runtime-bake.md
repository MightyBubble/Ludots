# Mass Navigation 尺寸、精度与 Runtime Bake 说明

> 更新时间：2026-06-08  
> 适用入口：`MassNavigationU16BakeToolQueryShowcaseMod`、`MassNavigationMod` runtime bake/query 链路。

## 当前结论

`km` 只是世界观和展示单位，不是引擎内部精度单位。Ludots 当前导航、障碍、寻路、输入点位、dirty bounds、path cache key 的实际坐标单位都是 **厘米整数 `world cm`**。

截至 2026-06-08 本轮复测，`MassNavigationU16BakeToolQueryShowcaseMod` 的 runtime/mod 自动化验收已经覆盖并通过：

- `GroundLight` 256 x 256 full-world bake：`BakeNavRecastLhtm done. ok=65536 fail=0`；
- 65,536 个 `layer0/profile_GroundLight` `.ntil` navtile 全量重写完成；
- 256 x 256 live NavMesh endpoint/path 验收；
- runtime authored polygon obstacle dirty tile rebake；
- bake 前路径进入 polygon、bake 后同起终点路径绕开 polygon；
- `NavTileStore.Revision` 变化后 path cache 失效并重新查询；
- repeated path query cache、32 并发 path query、large-world query budget benchmark。

这表示 runtime bake/query/cache 的自动化闭环已经通过。仍需单独做人工 UAT 的是实际 Raylib 窗口里的手感和可读性：小地图切换、摄像机缩放、NavMesh overlay 密度、按钮状态和玩家操作节奏。

## 当前世界尺寸

当前 `mass_navigation` map 的 board 配置在：

- `mods/capabilities/navigation/MassNavigationMod/assets/Maps/mass_navigation.json`

关键配置：

```json
{
  "WidthInTiles": 250,
  "HeightInTiles": 250,
  "GridCellSizeCm": 100,
  "ChunkSizeCells": 64
}
```

`NodeGraphBoard` / `GridBoard` 的世界尺寸公式是：

```text
worldWidthCm  = WidthInTiles  * 256 * GridCellSizeCm
worldHeightCm = HeightInTiles * 256 * GridCellSizeCm
worldBounds   = [-worldWidthCm / 2, -worldHeightCm / 2, worldWidthCm, worldHeightCm]
```

所以当前实际 runtime 世界是：

```text
worldWidthCm  = 250 * 256 * 100 = 6,400,000 cm = 64,000 m = 64 km
worldHeightCm = 250 * 256 * 100 = 6,400,000 cm = 64,000 m = 64 km
worldMin      = (-3,200,000 cm, -3,200,000 cm)
worldMax      = ( 3,200,000 cm,  3,200,000 cm)
```

## 每种尺寸不要混

| 名称 | 当前值 | 单位 | 用途 | 注意 |
| --- | ---: | --- | --- | --- |
| World coordinate | `int cm` | cm | 输入、寻路、障碍、overlay、path cache | 引擎真实精度 |
| World size | `6,400,000 x 6,400,000` | cm | runtime board bounds | 即 64km x 64km |
| Grid cell | `100` | cm | board/空间索引基础 cell | 1m |
| Board tile span | `256 * 100 = 25,600` | cm | board 尺寸公式里的 tile span | 不是 nav bake chunk |
| Streaming chunk | `ChunkSizeCells * GridCellSizeCm = 6,400` | cm | loaded chunks / streaming / solver window | 64m，和 nav macro chunk 不同 |
| Mass config `streamingChunkSizeCm` | `6,400` | cm | mass-navigation streaming 配置 | dirty nav tile 不按这个算 |
| Nav macro chunk count | `256 x 256` | chunk | bake diagnostics / U16 大世界验收 | 65,536 chunks |
| Runtime nav macro chunk size | `6,400,000 / 256 = 25,000` | cm | `NavTileStore.TileWidthCm/TileHeightCm` | 250m |
| LogicHeightmap chunk | `64 x 64` cells | cells | `.lhtm` baked tile source | 每 chunk 固定 4096 cells |
| LogicHeightmap cell | usually `100` | cm | height/area/blocked/ramp data | 由 `.lhtm` header 决定 |
| Baked nav tile local extent | `64 * 100 = 6,400` | cm | `.ntil` local vertices/portals/origin | bake-space，不等于 runtime nav chunk |
| Bake-to-runtime scale | `25,000 / 6,400 = 3.90625` | ratio | baked local -> runtime world cm | 当前看到比例差的核心原因 |
| Agent radius `GroundLight` | `30` | cm | Recast profile / clearance | 0.3m |
| Agent height `GroundLight` | `180` | cm | Recast profile | 1.8m |
| Runtime authored polygon | `Vector2 world cm` | cm | 用户点选障碍物 | bake 时映射到 baked absolute cm |
| Path endpoint | `PathEndpoint.WorldCm` | cm | 左右键起终点、World Path | path cache key 也用 cm |
| Path budget | `PathBudget.MaxExpanded` | count | portal/tile search budget | 大世界不能用小 smoke budget |

## 坐标系

Ludots runtime ground plane 使用二维 `world cm`：

```text
X: world east/west
Y: world north/south
unit: centimeter integer
origin: world center
bounds: WorldAabbCm.Left/Top/Right/Bottom
```

NavMesh 内部 tile 使用三类坐标：

```text
world cm:
  用户输入、path、overlay、PathService、cache key。

runtime tile local cm:
  world cm 减 worldMin，再按 runtime nav tile stride 定位 chunk。

baked tile local cm:
  .ntil 顶点、三角形、portal 的 local X/Z。
  当前每 tile 通常是 0..6400 cm。
```

`MassNavigationNavMeshRuntimeCoordinateMapper` 负责把 `.lhtm/.ntil` bake-space 映射回 runtime world-space：

```text
bakedWorldWidthCm  = bakedTileWidthCm * columns
runtimeWorldWidthCm = bakeDiagnostics.WorldWidthCm

worldXcm = worldMinXcm + bakedXcm * runtimeWorldWidthCm / bakedWorldWidthCm
```

当前 256 x 256 的 `.ntil` baked world 宽度约是：

```text
256 * 6,400 = 1,638,400 cm = 16.384 km
```

runtime world 宽度是：

```text
6,400,000 cm = 64 km
```

所以可视化、dirty tile、path endpoint 必须经过 mapper。任何直接把 baked local/origin 当 runtime world cm 的代码，都会出现“mesh 只显示一小坨”“dirty chunk 和 navmesh 比例不一样”“路径像在另一个国家”的症状。

## Runtime Bake 数据流

U16 当前目标链路应是：

```text
左键/右键或 minimap 选点
  -> Authoritative ground world cm
  -> MassNavigationRuntimeBakeAuthoringRuntime 记录 polygon world cm
  -> WorldToBakedAbsoluteX/Y 映射 polygon 到 baked absolute cm
  -> RecastNavTileBaker.TryBake(tileWindow, obstacles)
  -> NavTileStore.Replace(normalizedTile)
  -> NavTileStore.Revision++
  -> PathServiceRouter 清 cache
  -> NavQueryService / PathService 使用新 revision 查询
  -> overlay 显示新 navmesh/path
```

本轮自动化已验证：

- polygon authoring 能生成 dirty chunks；
- dirty chunks 能触发 Recast bake；
- `NavTileStore.Replace` 后 tile version/checksum/triangle count 会变化；
- UI diagnostics 能显示 baked/changed/source/dirty chunks；
- `PathServiceRouter` 在 baked tile count > 0 时会清 cache；
- bake 前后同一组 endpoint 与 authored polygon 有几何关系：bake 前进入 polygon，bake 后绕开 polygon；
- path cache key 包含 NavData revision，runtime rebake 后不会吃旧路；
- direct `Update NavData` 使用 runtime world endpoint resolver，不复用旧 smoke endpoint；
- full-world path 使用 256 x 256 live NavMesh，不退化成局部 5 x 5；
- repeated path query cache、32 并发 path query 和 large-world query budget benchmark 已覆盖。

## 为什么 dirty chunk 和 navmesh 比例看起来不同

它们不是同一种 chunk：

```text
streaming/solver chunk = 6,400 cm
runtime nav macro chunk = 25,000 cm
baked nav tile local extent = 6,400 cm
```

如果 UI 用 streaming chunk 去画 dirty 网格，但用 nav macro chunk 去画 navmesh，比例会不同。正确展示必须明确标注当前 overlay 用的是：

- `MassNavigationRuntimeDirtyChunkGrid.NavTile`：按 runtime nav macro chunk；
- streaming chunk：按 `ChunkSizeCells * GridCellSizeCm`；
- baked tile local：只能作为 `.ntil` 内部局部坐标，不能直接画到 world。

## Unity / Unreal 对照

Unity 的默认工程实践通常把 1 Unity world unit 当作 1 meter；Unity Transform 文档也说明物理引擎默认假设 world space 的 1 unit 对应 1 meter。Unity NavMesh build settings 用 agent radius/height/climb/slope 描述 agent，并用 voxel size / tile size 控制 bake 精度和分块；其中 voxel size 是 world length units，tile size 是 voxel units。Unity 官方还建议 runtime 更新时 tile size 大约 32-128 voxels，并说明更小 voxel 会提高精度但增加内存和 bake 时间。

Unreal Engine 默认 Distance/Length 单位是 centimeters。Unreal Recast NavMesh 也按 tile/cell 管理：Cell Size / Cell Height 是生成 navigation tiles 的 voxel 尺寸，越小精度越高但运行时 rebuild 更贵；Tile Size UU 决定 tile 尺寸，动态 navmesh rebuild 推荐每边 32-128 cells，并要求 Tile Size UU 能被各 Cell Size 整除以获得更好性能。

Ludots 当前更接近 Unreal 的单位策略：runtime 直接使用 centimeter integer，而不是 Unity 的 meter float world unit。差异是 Ludots 目前 `.lhtm/.ntil` bake-space 可以和 runtime world-space 不同尺度，必须由 mapper 统一换算。

官方参考：

- Unity `NavMeshBuildSettings`：<https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings.html>
- Unity `voxelSize`：<https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-voxelSize.html>
- Unity `tileSize`：<https://docs.unity3d.com/ScriptReference/AI.NavMeshBuildSettings-tileSize.html>
- Unity Transform / world unit scale：<https://docs.unity3d.com/2022.1/Documentation/Manual/class-Transform.html>
- Unreal Units of Measurement：<https://dev.epicgames.com/documentation/en-us/unreal-engine/units-of-measurement-in-unreal-engine>
- Unreal Coordinate System：<https://dev.epicgames.com/documentation/en-us/unreal-engine/coordinate-system-and-spaces-in-unreal-engine>
- Unreal Navigation Mesh Resolutions：<https://dev.epicgames.com/documentation/unreal-engine/navigation-mesh-resolutions-user-guide>
- Unreal NavMesh generation speed：<https://dev.epicgames.com/documentation/unreal-engine/optimizing-navigation-mesh-generation-speed-in-unreal-engine>

## 验收口径

后续任何人说“runtime bake 没问题”，必须同时满足：

1. polygon obstacle 在 runtime world cm 中可见；
2. dirty chunks 使用 nav tile world bounds，且和 navmesh overlay 对齐；
3. dirty tile rebake 后 `NavTileStore.Revision` 增加；
4. path cache miss/hit 符合 revision 失效规则；
5. bake 前后同起终点 path 与 polygon hole 有几何关系；
6. 256 x 256 full-world live NavMesh route 通过；
7. benchmark 覆盖 repeated query cache、并发 query、large-world query budget。

本轮自动化复测已经满足上面 1-7。人工 UAT 仍应继续检查实际窗口交互是否符合玩家预期，但这不再是 runtime bake/query/cache 链路本身的阻塞项。
