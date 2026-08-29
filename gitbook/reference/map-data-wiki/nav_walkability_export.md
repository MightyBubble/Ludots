# 导出一张和棋盘对齐的走性图

玩家按 `T` 时，地上那一层颜色不是手绘贴图，是导航瓦片当场栅格化出来的。作者先有 `.ntil`，再导出一张和棋盘厘米框对齐的 PNG。

## 作者写法

第一次来的地图作者看这里：命令从仓库根目录跑，工具是 `Ludots.Tool` 的 `nav export-walkability-texture`。缺目录、缺瓦片、框没包住顶点，一律直接失败，不会 silently 裁掉一块海。

| 项 | 值 |
|----|----|
| 入口 | `dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav export-walkability-texture` |
| 输入 | 一目录 `.ntil`，或 `--mapId` + `--profile`（再加 `--modId` / `--layer` / `--repoRoot`） |
| 输出 | PNG + 同名旁路 JSON（`*.png.json`） |
| 世界单位 | 厘米。`--minXcm` / `--minZcm` / `--maxXcm` / `--maxZcm` 四个一起给，少一个就炸 |
| 像素 | `--width` 必填正数；`--height` 为 0 时按框的南北/东西比推高 |

真实用例（东亚约 64 km 棋盘，框与地图元数据同一组厘米）：

```text
dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- nav export-walkability-texture \
  --mapId east_asia_visual_heightmap \
  --modId EastAsiaNavMeshDebugMod \
  --profile Small \
  --repoRoot . \
  --out <mod>/assets/Textures/nav_walkability.png \
  --width 4096 \
  --minXcm -3199616 \
  --minZcm -1828352 \
  --maxXcm 3199616 \
  --maxZcm 1828352
```

已经有瓦片目录、不想走地图解析时，改用 `--inDir` 指向装 `.ntil` 的文件夹。`--inDir` 和 `--mapId`+`--profile` 必须二选一，两个都不给会失败。

## 这条链路怎么走

```text
棋盘 + 高度场
  → nav bake-vhtm（或其它正式烘焙）写出 .ntil
  → export-walkability-texture 读全部瓦片
  → 按厘米框把三角形铺到像素
  → 写出 PNG 和旁路 JSON（框、宽高、编码说明、内容哈希）
```

导出器做的事：

1. 按路径排序读完目录里所有 `.ntil`，一张都没有就失败。
2. 没有显式厘米框时，用全部可走顶点推出包围盒；一个可走顶点都没有，必须自己传框，否则失败。
3. 显式框必须包住每一颗顶点，露出去就失败——不会偷偷裁。
4. `--height` 为 0 时：`height = max(1, round(width * 框南北 / 框东西))`。东亚那份 4096 宽会得到 2341 高。
5. 每个导航三角形按地类编号上色，透明度 255；没盖到的像素保持全透明。
6. 可选 `--paintLandBlockedWater`：还要 `--mapId` 和 `--vhtm`。逻辑地形上标记为阻断或有水、且还是透明的像素，涂成固定的陆上阻断红。已经可走的陆地像素不会被盖掉。
7. PNG 旁边写出 `schemaVersion = 1` 的 JSON，带 `contentHash = sha256:…`。哈希对整份 PNG 字节，改一个像素就会对不上。

## 边界与更多用法

- 这一步不替代烘焙。没 `.ntil` 就没有走性图。烘焙合同见 [Navmesh 作者工具链](../navmesh-authoring-bake-toolchain.md)。
- 像素含义见 [像素怎么编码](nav_walkability_encoding.md)；挂到地图见 [地图上怎么挂这张走性图](nav_walkability_config.md)。
- `--layer` 默认 0，和瓦片目录 `layer0` 对齐。换层就要换目录，不会回落到别的层。
- 框的四个厘米必须同时出现。只给其中几个会失败。
- 涂阻断水面时，`--seaLevelCm` 可以压过棋盘的「低于此高度算阻断」。不传就用棋盘自己的数。

## 怎么跑

导出后，用带走性叠加的东亚调试预设进游戏，按 `T` 看这张图是否钉在地形上：

```text
scripts/run-mod-launcher.cmd cli launch 'preset:east_asia_navmesh_debug_raylib'
```
