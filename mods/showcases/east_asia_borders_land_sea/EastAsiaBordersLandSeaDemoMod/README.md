# East Asia Borders · Land & Sea

玩家在约 64 km 东亚棋盘上：

1. 看到 **国界色贴在地形上**（投影贴花，不是调试格网）
2. 点选 **陆军**（黄块，开局在中国境内）右键走陆地导航网
3. 点选 **航船**（蓝块，开局在黄海海面）右键沿海面导航网行驶，不会抄近路穿陆地
4. 单位跨入不同国家时，左上角面板刷新过境代码与次数

导航三角网 / 走性贴图是调试层，本演示默认不开。需要时用 `preset:east_asia_navmesh_debug_raylib`，按 `N` / `T`。

陆军走 Ground 层，航船走 Water 层。内河 PreferGraph 仍需要 NodeGraph 主棋盘，记为后续债。

## Launch

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:east_asia_borders_land_sea_raylib'
```

## Controls

- 左键选中陆军或航船
- 右键地面下移动令（`massNavigationMove`）

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

国界格子由 `tools/east_asia_borders/rasterize_countries_to_field.py` 生成。
玩家看见的颜色来自同目录 `export_country_decal_png.py` 画的 Decal 贴花。
