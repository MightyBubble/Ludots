# 配置文件规范

本项目采用配置驱动的开发模式。所有游戏数据（地图、实体模板、掉落表等）均通过配置文件定义。

## 1. 目录结构
```
/assets
  /Configs
    /Maps            # 地图配置文件 (*.json)
    /Entities        # 实体模板文件 (*.json)
    /GAS             # 能力系统配置（时钟、模板、Graph 等）
  /Data
    /Maps            # 二进制地图数据 (*.bin)
```

## 2. 实体模板 (Entity Template)
实体模板定义了一类实体的默认 Component 集合。
*   **格式**: JSON
*   **位置**: `assets/Configs/Entities/`
*   **Schema**:
    ```json
    {
      "id": "orc_warrior",
      "components": {
        "Health": { "current": 100, "max": 100 },
        "Attack": { "value": 10 },
        "Sprite": { "assetId": "orc_idle" }
      }
    }
    ```

## 3. 地图配置 (Map Config)
地图配置定义了地图的元数据、引用的二进制地形文件以及初始化的实体列表。
*   **格式**: JSON
*   **位置**: `assets/Configs/Maps/`
*   **Schema**:
    ```json
    {
      "id": "level_1_forest",
      "dataFile": "level_1.bin",  // 引用 assets/Data/Maps/level_1.bin
      "entities": [
        {
          "template": "orc_warrior",
          "position": { "x": 10500, "y": 20000 }, // 对应 IntVector2
          "overrides": {
            "Health": { "current": 200, "max": 200 } // 覆盖模板默认值
          }
        },
        {
          "template": "tree_pine",
          "position": { "x": 1000, "y": 1000 }
        }
      ]
    }
    ```

## 4. 二进制地图数据 (*.bin)
为了支持 16384x16384 的大地图，地形数据（高度、阻挡）存储为二进制文件。
*   **Header**:
    *   `Magic`: 4 bytes ("LMAP")
    *   `Version`: 4 bytes (int)
    *   `Width`: 4 bytes (int, tiles count = 64)
    *   `Height`: 4 bytes (int, tiles count = 64)
*   **Body**:
    *   按 Tile 顺序 (0,0 -> 63,0 -> ... -> 63,63) 存储。
    *   每个 Tile:
        *   `HeightData`: 256*256 bytes (0-255)
        *   `BlockData`: 256*256 bits (packed) OR bytes (for simplicity first)

## 5. React Web 编辑器导出（map_data.bin）
React Web 编辑器（`src/Tools/Ludots.Editor.React`）当前导出的二进制地图文件名固定为 `map_data.bin`，并且其数据布局与上面的 `LMAP` 规范不同，不能直接互换使用。

*   **Header（9 bytes）**:
    *   `WidthInChunks`: 4 bytes (int32 LE)
    *   `HeightInChunks`: 4 bytes (int32 LE)
    *   `StrideOrVersion`: 1 byte（当前实现固定为 `2`）
*   **Body**:
    *   以 `64x64` chunk 为单位顺序写出（chunk 顺序为 cy 外层、cx 内层）。
    *   每个 cell 为 4 bytes（`Uint8Array`），其中与地形相关的字段为：
        *   Byte0：Height=高 4 bits（0..15），Water=低 4 bits（0..15）
        *   Byte1：Biome=高 4 bits（0..15），Veg=低 4 bits（0..15）

## 6. TerrainType 语义与可行走性
`Core/Navigation/Analysis/TerrainAnalyzer` 对 “TerrainType（t1/t2/t3）→ Water/Blocked 等语义” 不在 Core 内硬编码具体 ID，而是通过 lookup 表提供机制：

*   **接口**:
    *   `ITerrainTypeLookup`（C#）：`TerrainTypeProperties Get(byte terrainType);`
    *   `TerrainTypeProperties` 示例字段：`IsBlocked`, `IsWater`（可按需要扩展，如 `IsLava`、`MoveCostModifier` 等）。
*   **默认实现**:
    *   `DefaultTerrainTypeLookup`：所有类型均视为“非水、非阻挡”（即 TerrainAnalyzer 仅依据高度差判定 Cliff/可行走），用于未配置情况下的安全默认。
*   **调用点**:
    *   `TerrainAnalyzer.AnalyzeTriangle` 在高度差计算前会先查询三点的 `TerrainType`：
        *   若任意点 `IsBlocked==true` → 直接返回 `Blocked`。
        *   否则若任意点 `IsWater==true` → 返回 `Water`。
        *   否则回落到原有的高度差规则（0/1 台阶可走，≥2 视为 Cliff）。
*   **配置责任**:
    *   Core 不维护任何「biomeId → 语义」硬编码映射，**TerrainType 的语义由上层负责**：
        *   React 编辑器 / World 生成脚本 / 配置管线应产出一份 `terrain-type-lookup` 表（JSON 或代码注册），将 biome/terrain ID 映射为 `TerrainTypeProperties`。
        *   运行时将该表注册为 `ITerrainTypeLookup` 的实现并注入 `TerrainAnalyzer` 使用。
    *   若未提供自定义 lookup，将使用默认实现，不产生 Water/Blocked 判定，仅根据高度差区分 walkable / cliff。

## 7. React 导出到 VertexMapBinary（调试用）
为了快速在 Core 侧复用 `VertexMap/VertexChunk` 的数据布局进行调试（不依赖 LMAP tile 体系），提供了一个调试用二进制格式 `VertexMapBinary`，以及配套转换工具命令。

*   **格式（VTXM v1）**:
    *   Header：
        *   `Magic`: 4 bytes ("VTXM")
        *   `Version`: 4 bytes (int32, =1)
        *   `WidthInChunks`: 4 bytes (int32)
        *   `HeightInChunks`: 4 bytes (int32)
        *   `ChunkSize`: 4 bytes (int32, =64)
        *   `Reserved`: 4 bytes (int32, =0)
    *   Body：按 chunk 顺序（cy 外层、cx 内层）写出，每个 chunk 依次包含：
        *   `PackedData`: 4096 bytes（Biome 高 4 bits + Height 低 4 bits）
        *   `Layer2`: 4096 bytes（Veg 高 4 bits + Water 低 4 bits）
        *   `Flags`: 512 bytes（4096 bits，ulong[64] little-endian）
        *   `RampFlags`: 512 bytes（同上）
        *   `Factions`: 4096 bytes
*   **转换命令**（repo 根目录运行）:
    *   `dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- map import-react --in <path-to-map_data.bin> --name <mapId> --force`
    *   默认输出到 `assets/Data/Maps/<mapId>.vertexmap.bin`，并生成一个 `*.summary.json`（用于快速核对高度/水位范围与分布）。

## 8. GAS 时钟配置（Step 频率）

GAS 的“关键逻辑帧边界”使用离散 Step tick 表达。Step 的推进频率不写死在代码里，而由配置与运行时策略控制。

* **位置**：`assets/Configs/GAS/clock.json`
* **字段**：
  * `stepEveryFixedTicks`：每多少个 FixedTick 推进一次 Step
    * `1`：Step 与 FixedTick 同频（例如 FixedTick=60Hz 则 Step=60Hz）
    * `6`：Step=10Hz（假设 FixedTick=60Hz）
* **合并规则**：配置通过 `ConfigPipeline` 收集 Core + Mods 的多个片段并合并，允许 Mod 覆盖 Core 默认值。

*   **运行时加载（Engine）**:
    *   `MapConfig.DataFile` 可直接指向 `*.vertexmap.bin`（建议写相对路径：`Data/Maps/<mapId>.vertexmap.bin`）。
    *   `GameEngine.LoadMap(mapId)` 在读取 `MapConfig` 后，会尝试通过 VFS 从 Core/Mods 中打开该文件并加载为 `GameEngine.VertexMap`。
    *   脚本上下文 `ScriptContext` 会注入 `ContextKeys.VertexMap`，可用于 map loaded 事件或调试脚本直接读取地形数据（高度/水面/biome/veg 等）。

*   **MapConfig 示例**:
    ```json
    {
      "Id": "TestMap",
      "DataFile": "Data/Maps/TestMap.vertexmap.bin",
      "Entities": []
    }
    ```
