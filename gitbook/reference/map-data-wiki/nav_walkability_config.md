# 地图上怎么挂这张走性图

贴图躺在 Mod 里还不会出现在地上。地图必须写明「用哪张图、盖住哪一块厘米」，进游戏后按 `T` 才铺到地形上。

## 作者写法

第一次来的地图作者看这里：在地图 JSON 的 `Metadata` 里写 `navWalkabilityOverlay`。键名是合同，不能改成别的英文。

| 项 | 值 |
|----|----|
| 元数据键 | `navWalkabilityOverlay` |
| `textureUri` | Mod 资源 URI，例如 `EastAsiaNavMeshDebugMod:assets/Textures/nav_walkability.png`。不能空、不能前后空格 |
| `boundsCm` | `minX` / `minZ` / `maxX` / `maxZ`，厘米，min 必须小于 max |
| 旁路优先 | 贴图旁边若有 `*.png.json`，框只读旁路；没有旁路才读地图里的 `boundsCm` |
| 开关 | 调试输入按 `T` 打开走性投影；地图建议带 tag `walkability-texture` 方便检索 |
| 缺了就炸 | 打开走性投影时没有这段元数据、URI 解不出、贴图文件不在，直接失败 |

东亚棋盘上的真实写法（框与导出命令同一组厘米）：

```json
{
  "Id": "east_asia_visual_heightmap",
  "Tags": ["walkability-texture"],
  "Metadata": {
    "navWalkabilityOverlay": {
      "textureUri": "EastAsiaNavMeshDebugMod:assets/Textures/nav_walkability.png",
      "boundsCm": {
        "minX": -3199616,
        "minZ": -1828352,
        "maxX": 3199616,
        "maxZ": 1828352
      }
    }
  }
}
```

旁路 JSON 一旦存在，上面的 `boundsCm` 只作作者备忘；运行时以旁路为准。改框要重新导出，不要只改地图、不改旁路。

## 这条链路怎么走

```text
地图 Metadata.navWalkabilityOverlay
  → 解析 textureUri 成磁盘路径
  → 有旁路则读旁路 boundsCm，否则读地图 boundsCm
  → 把贴图和厘米框交给地形渲染
  → 玩家按 T：走性色按 35% 透明度混进地表
  → 远景总览网格同样吃这组框；关掉走性时只撤走性，不撤地表纹理
```

`N` 看的是烘焙三角网，`T` 看的是这张投影。两套开关，不要混成一个。调试按键由显式挂上的调试启动 Mod 提供；数据 Mod 只提供瓦片和贴图，不自己抢按键。

打开走性投影时如果当前没有已加载的地图，失败。已经铺上之后再按一次 `T`，应清掉叠加，不能留残色。

## 边界与更多用法

- 这是检查层，不是第二种寻路。改 PNG 不会改部队能走哪里。
- 框必须和导出时同一组厘米，否则颜色会在地上整体错位。编码合同见 [像素怎么编码](nav_walkability_encoding.md)。
- 重新烘焙瓦片后必须重新导出贴图，并确认旁路哈希更新。步骤见 [导出一张和棋盘对齐的走性图](nav_walkability_export.md)。
- 远景如果还开着距离雾，整片会被洗蓝，走性色也看不清——那是雾的问题，不是贴图没挂上。大棋盘调试应关掉距离雾。
- 国界色、地表权重图是另一路投影，不要把走性图当成国界图用。

## 怎么跑

```text
scripts/run-mod-launcher.cmd cli launch 'preset:east_asia_navmesh_debug_raylib'
```

进图后：`T` 打开，陆地应出现地类色；再按 `T` 关掉，地表应回到原来的样子。`N` 只动三角网，不应把走性一起关死。
