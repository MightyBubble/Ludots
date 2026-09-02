# TerrainExport(DefaultMap.bin → Ludots 地形资产)

`mods/sango/SangoContentMod/TerrainExport/`(命名空间 `Sango.Content.TerrainExport`)把 M0.2 的
`SangoMapBinArchive` 离线转换成 Ludots 地形管线资产,供 `SangoTerrainMod` 挂载:

- `SangoTerrainExporter.ExportToDirectory(archive, dir)` →
  - `sango_default.height`:CHTM v2(`ContinuousHeightmapBinary`,RowMajorInt16Centimeters)单层可视地形;
  - `sango_default_hex.hex`:HexGridBoard 的 DataFile(`VertexMapBinary` v2,HEXM)。
- `SangoTerrainScale`:换算常数、海平面推导(`DeriveSeaLevelCm`)、terrainType→biome 视觉投影、
  0..255→0..15 nibble 量化。

## 路线选择:离线转换(不是运行时注入)

引擎两条加载路径都只认文件:`GameEngine.LoadVertexMapFromFile` 与
`MapContinuousHeightmapLoader.Load` 均通过 `VFS.GetStream` 打开已挂载资产,没有公开的运行时注入
Board VertexMap / ContinuousHeightmap 的 API。因此选离线:转换产物落
`mods/sango/SangoTerrainMod/assets/terrain/`,由仓库根 `.gitignore` 精确排除(源 bin 是 KOEI 派生的
版权内容,产物一律不入库);真实 bin 路径只出现在工具/测试参数里。

再生成(工作区本地,不入库的 scratch runner):

```
dotnet run --project C:\001_AI\SangoPort\tmp\m03export -- C:\001_AI\SangoPort\content\DefaultMap.bin ^
  C:\001_AI\SangoPort\ludots\mods\sango\SangoTerrainMod\assets\terrain
```

## 坐标系与轴向

sango `MapData.VertexPosition = (y * quadSize, height * 0.5f, x * quadSize)`:顶点 x 是北轴,
顶点 y 是东轴。导出按此转置:CHTM sample 列(世界 X/东)= 源顶点 y,采样行(世界 Z/北)= 源顶点 x;
HexGrid 的 VertexMap col(东)= 格子 y,row(北)= 格子 x。地图 bounds 以世界原点居中
(-256000..+256000cm),与 HexGridBoard 世界范围对齐。

## 高度与水面换算(源单位 → cm)

- 1 源高度单位 = 0.5 m = **50 cm**(`CmPerHeightUnit`),与原作 `height * 0.5f` 一致;1 源世界单位
  (quadSize 单位边)= 100 cm,真实图 1024×5 = 5120 m = **512000 cm** 见方。
- 海平面:取全图 water 字节众数(真实图为 11 → **550 cm**)。`SangoTerrainMod` 的
  `RenderProfile.SeaLevelCm=550` 与 `water_environments.json` 的 `waterPlaneY=5.5`(米)必须与此一致,
  headless 测试对真实 bin 重新推导并断言。
- 水面顶点(`water > 0`)的 CHTM 高度 = `min(height, 海平面)`:海床保留在海平面之下,由引擎的
  绝对着色把 ≤ 海平面的样本显示为平面海面并按深度上蓝(近岸浅绿→深海深蓝),因此海岸线可辨;
  高于海平面的河段(如 water=14..16 的河流、55+ 的高地湖)保持河床/湖盆地形,河谷走向可辨。
  该规则是任务书中"water 顶点置海平面"的落地形式:严格置平会把海洋染成滩涂色,违背
  "海岸线可辨"的验收目标。
- 观感标定:陆地峰值 255 → 12750 cm,`AbsoluteColorPeakSpanCm = 12200`(峰值−海平面),配合引擎
  绝对高程色带(滩→草→土→岩→雪顶),平原(中位 75→3750cm,band≈0.26)落在草/土带,山脉顶部到
  岩/雪带,山脉/平原比例与原作 Unity 地形(127.5m/5120m)一致,`DisplayHeightScale=1`。

## HexGrid DataFile 的格子数据

真实图 grid 256×256(gridSize 20 / quadSize 5 → gridVertexCount 4)→ VertexMap 4×4 chunk
(64×64/chunk)。每格:

| VertexMap 层 | 内容 |
|---|---|
| height nibble | 格中心顶点高度 0..255 → 0..15 线性量化 |
| water nibble | 格中心顶点 water 字节同法量化(water>height 即被 logic terrain 视为水面) |
| biome nibble | `ProjectTerrainTypeToBiome`:terrainType → 引擎 6 色的可视投影(仅兜底 hex 顶点色;CHTM 存在时渲染走 CHTM) |
| extra byte layer 0 | **原始 TerrainTypes.json terrain 字节(0..255 无损)**,M1 的 `sango.terrainType` Field2D 层以此为准 |

areaId(92 个区域)未入 DataFile(无对应层);走 Field2D persistent 层是 M1 的事,见汇报遗留清单。
