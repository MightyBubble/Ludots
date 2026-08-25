# 环境与光照逐条

环境面全部由引擎画廊的场景化验收覆盖（每场景 = 一键 preset + 截图 + 120 帧统计 + 页内可播录像）。总目录见 [README.md](README.md)。逐场讲解见 [引擎画廊 Wiki](../engine-gallery-wiki/README.md)，光照栈合同见 [渲染光照栈与下游使用指南](../../architecture/render-lighting-guide.md)。

| 能力 | 一句话 | preset | 证据 |
|---|---|---|---|
| PBR 光照 | Cook-Torrance GGX 粗糙度×金属度 + split-sum IBL | `engine_raylib_lighting` | `artifacts/acceptance/engine_raylib_lighting/screen.png` |
| 光照总线昼夜 | 光向/光色/环境/雾随昼夜相位驱动 | `engine_raylib_frame_lighting` | 同目录 `screen.png` + `stats.json` |
| 方向光阴影 | 深度图 + PCF；Cutout 材质 alpha 打孔树影 | `engine_raylib_lighting` / `engine_raylib_vegetation_cutout` | 两目录 `screen.png` |
| 天空盒 | 程序化天空盒 + 太阳盘/光晕 | `engine_raylib_skybox` | 同目录 `screen.png` |
| 昼夜渐变天空 | 相位步进的渐变天空（四十八秒过完一整天） | `engine_raylib_sky_daynight` | `artifacts/evidence/engine_raylib_sky_daynight/play.mp4` |
| 水体 | 反射/折射双 RT + 波动法线 | `engine_raylib_water` | 同目录 `screen.png`，录像 `play.mp4` |
| 距离雾 | 配置驱动的距离雾四参数 | `engine_raylib_atmosphere_fog` | 同目录 `screen.png` |
| 后处理 | 曝光/对比/饱和/暗角 | `engine_raylib_postprocess` | 同目录 `screen.png` |
| 植被镂空 | Billboard cutout + 打孔阴影 | `engine_raylib_vegetation_cutout` | 同目录 `screen.png` |
| 地形（表面） | chunk surface 车道 | `engine_raylib_terrain_surface` | 同目录 `screen.png` |
| 地形（高度图） | `.vhtm` 高度图 + 色带 | `engine_raylib_terrain_heightmap` | 同目录 `screen.png` |

环境配置树（雾/环境光 ramp/阴影参数/天空/水体条目）的作者面字段与装载规则见 [Raylib 渲染配置结构](../raylib-render-config-structure.md)；材质系统三轴（换贴图/换 shader/换参数）同见该页。
