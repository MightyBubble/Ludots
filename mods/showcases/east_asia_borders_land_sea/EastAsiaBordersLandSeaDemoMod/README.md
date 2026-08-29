# East Asia Borders · Land & Sea

玩家在约 64 km 东亚棋盘上：

1. 看到 **Natural Earth 国界** 半透明铺在地形上（不是矩形示意框）
2. 点选 **陆军**（黄块，开局在中国境内）右键走陆地导航网
3. 点选 **航船**（蓝块，开局在黄海海面）右键直线驶向目标
4. 单位跨入不同国家时，左上角面板刷新过境代码与次数

航船当前是直线移动：本图主棋盘是陆地 NavMesh；河网 PreferGraph 需要 NodeGraph 主棋盘，记为后续债。

## Launch

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:east_asia_borders_land_sea_raylib'
```

## Controls

- 左键选中陆军或航船
- 右键地面下移动令（`massNavigationMove`）
- `N` 导航三角网 / `T` 走性贴图（`NavMeshDebugLaunchMod`，由 Raylib preset 显式挂载）

## 过境面板代码

| regionCode | 国家 |
| --- | --- |
| 1 | 中国 |
| 2 | 日本 |
| 3 | 韩国 |
| 4 | 朝鲜 |
| 5 | 越南 |
| 6 | 俄罗斯 |
| 7 | 蒙古 |
| 8 | 台湾 |

## Data

国界由 `tools/east_asia_borders/rasterize_countries_to_field.py` 从 Natural Earth 110m
按 `east_asia_terrain_profile.json` 的 Albers + `WorldWidthCm` 栅格化生成，写入
`FieldEastAsiaCountryMod` 的 `ownership.east_asia.country`。
