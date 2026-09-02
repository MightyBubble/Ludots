# DefaultMap.bin 字节布局（MapBin 读写器）

`mods/sango/SangoContentMod/MapBin/` 是 sango `DefaultMap.bin` 二进制地图格式的纯 C# 读写器（命名空间 `Sango.Content.MapBin`），供 M0.3 地形转换器读取真实地图数据。格式源码：`sango-src/Project/Assets/Sango/Scripts/Map/Render/Map/MapRender.cs` 的 `LoadMap`/`SaveMap`（`VERSION = 10`），各段 `OnLoad/OnSave` 在同目录 `MapGrid.cs`/`MapData.cs`/`MapLayer.cs`/`MapTerrain.cs`/`MapLight.cs`/`MapFog.cs`/`MapBaseColor.cs`/`MapSkyBox.cs`/`MapCamera.cs`/`MapModels.cs`/`MapLabelSet.cs`。

入口：`SangoMapBinIo.Read(BinaryReader)` / `SangoMapBinIo.Write(BinaryWriter, archive)`。`Read` 覆盖 v1–v10 全部版本分支；`Write` 只产出 v10 布局（源 `SaveMap` 也固定写当前 VERSION）。未序列化的 Unity 派生量（mesh/法线/UV/材质/Burst Job）不在本包内。

## 容器头

| 字段 | 类型 | 条件 |
|---|---|---|
| version | int32 | 当前 10 |
| WorkContent | string | v ≥ 6 |
| （丢弃一个 int32） | int32 | v ≤ 2 |
| mapWidth | int32 | |
| mapHeight | int32 | |

string 均为 .NET `BinaryWriter` 格式：7-bit 长度前缀 + UTF-8。

段顺序 v ≥ 6：mapGrid → mapData → mapLayer → mapTerrain → mapLight → mapFog → mapBaseColor → mapSkyBox → mapCamera → mapModels → mapLabelSet。v ≤ 5 时 mapGrid 挪到 mapSkyBox 之后（且先读 gridTextureName）。

## 各段布局（按 v10 主路径，旧版分支列在段内）

### mapGrid（六边格，odd-q 排列）

- v < 6 先读 string gridTextureName；v ≥ 6 在段尾读。
- int32 gridSize（读入值与 quadSize 取 max 后存回，保存时写取 max 后的字段值）。
- 格子数：`boundsX = mapWidth * quadSize / gridSize`，`boundsY = mapHeight * quadSize / gridSize`。注意 v ≥ 6 时 mapGrid 在 mapData 之前加载，quadSize 还是 MapData 的字段默认值 5，不是流里后存的 quadSize；v ≤ 5 时用已读入的 quadSize。
- 每格 7 字节：byte terrainType（TerrainTypes.json 索引）+ int32 terrainState（v ≥ 7；位掩码 Defence=1<<1、Interior=1<<2、Thief=1<<3）+ uint16 areaId（v ≥ 8，区域 ID）。v < 5 时额外：int32 丢弃 + v > 0 时 11 字节 San11GridData（tType/areaId/lpB/trap/dir/interior/defence/thief/flood/fire/ruins 各 1 字节），读入后折算为 terrainType=tType+1、areaId=areaId+1、terrainState 三个状态位。
- 遍历顺序 x 外层、y 内层（`gridDatas[x][y]`）。

### mapData（顶点网格）

- int32 quadSize（v ≤ 2 不读，固定 5）。
- `(mapWidth+1) * (mapHeight+1)` 个顶点，每个 3 字节：byte height（世界高度 = height * 0.5）、byte textureIndex（地形图层索引）、byte water。x 外层、y 内层。

### mapLayer（地形材质层）

- int32 layerCount；每层：bool isLit、float textureScale.x、float textureScale.y、4 季 ×（string diffuse 名、string normal 名、string mask 名）。季节顺序 Autumn/Spring/Summer/Winter；数组最后一层是水面层。空名写空串。

### mapTerrain

- int32 cellSize（地形渲染分块尺寸，仅此一个值）。

### mapLight

- 固定 4 季 × 11 个 float：light_direction x/y/z、light_color r/g/b、light_intensity、shadow_color r/g/b、shadow_strength。

### mapFog

- 固定 4 季 × 6 个 float：fog_color r/g/b、fog_start、fog_end、fog_density。

### mapBaseColor

- v > 5：流里 0 字节。四季底图是 bin 同目录的 sidecar 文件 `BaseTex/BaseMap0..3`。`OnSave` 也不写流（Unity 把 RenderTexture 导出成旁路 PNG），因此 `Write` 为空操作。
- v == 5：4 ×（int32 length；length > 0 时 int32 width、int32 height、byte[length] PNG）。
- v < 5：4 × string。

### mapSkyBox

- v == 1：float、float、int32 count、count × string（丢弃）、int32（丢弃）。
- 其他版本：float blendStart、float blendEnd、int32 count，每区域 float bounds x/y/w/h + 4 × string 季节贴图名。
- 源 `OnSave` 写的是从未被赋值的 `sky_blend_start`/`sky_blend_end`（-200f/-150f）常量，不是读回的 blendStart/blendEnd；`Write` 照原样复刻。

### mapCamera

- v ≤ 2：17 个 float 全部丢弃。v ≥ 9：float safeBoder（源字段名，camera 视野安全边距；v < 9 时保持默认 560f）。其余版本 0 字节。

### mapModels（场景模型）

- int32 count；每模型：int32 objId、int32 objType、int32 bindId（v ≥ 4 才有；绑定玩法对象，0 为无）、int32 modelId（ModelConfig.json 索引）、3 float position、3 float rotation（欧拉角）、3 float scale。

### mapLabelSet（地图文字标注）

- v ≥ 10 才有：int32 count；每标注：string labelText、3 float position、byte r、byte g、byte b、int32 fontSize。v < 10 段内无数据。

## 给 M0.3 的提示

- Unity `Color`/`Color32` 在此格式里只序列化 RGB（float 3 通道 / byte 3 通道），alpha 从不落盘；本包对应 `SangoRgbF`/`SangoRgb32`。
- `Vector2/Vector3` 已映射为 `System.Numerics`；格子/顶点数组都是 x 外层、y 内层的一维展开（`CellAt`/`VertexAt` 提供索引）。
- 高度数据在 mapData（byte，×0.5 得世界高度），格子归类在 mapGrid.terrainType，两者坐标系的换算走 `gridVertexCount = gridSize / quadSize`。
