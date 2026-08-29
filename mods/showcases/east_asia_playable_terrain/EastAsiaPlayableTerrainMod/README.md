# EastAsiaPlayableTerrainMod

东亚可玩地形（grid / hex / visual heightmap 三入口）。大体积地形资产不进本仓库，由
[LudotsSample](https://github.com/MightyBubble/LudotsSample) 以 git submodule 提供
（`assets/samples/LudotsSample`，vhtm/vtxm 走 Git LFS）。

## 首次拉取

```bash
git submodule update --init mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/samples/LudotsSample
git lfs pull --include "mods/showcases/east_asia_playable_terrain/EastAsiaPlayableTerrainMod/assets/samples/LudotsSample/*"
```

缺失实体时地图装载/相关合同测试会直接失败（无 fallback）。

## 资产再生成

Ludots.Tool 的 `EastAsiaTerrainAssetGenerator` / `TerrainControlMapBaker` 可再生等价资产，
输出需指向 submodule 目录；规范路径见本 mod 各 `assets/Maps/*.json` 的
`VisualHeightmapAsset` / `DataFile` 引用（`assets/samples/LudotsSample/east_asia/…`）。
