# 地图数据 Wiki

地图上能看见的格子、高度、走性，都先是一份对齐世界厘米的数据，再被画上去。本 wiki 按作者任务分页：先会导出，再看像素怎么编码，最后挂到地图上。排版跟 [Graph 节点画廊](../graph-node-op-wiki/README.md) 同一套——人话标题、作者写法表、这条链路怎么走、边界、怎么跑。

目录由 `scripts/build-site.py` 解析本页生成门户侧栏；门户一级 tab 是「地图数据」。烘焙三角网本身仍看 [Navmesh 作者工具链](../navmesh-authoring-bake-toolchain.md)，这里只收「烤完之后怎么变成可贴的走性图」。

## 导航走性贴图

- [导出一张和棋盘对齐的走性图](nav_walkability_export.md) — 从导航瓦片烤出 PNG，框住整张棋盘的厘米范围。
- [像素怎么编码：颜色、透明、哪一行是北](nav_walkability_encoding.md) — 红绿蓝表示地类，透明表示不能走，第一行是北沿。
- [地图上怎么挂这张走性图](nav_walkability_config.md) — 地图元数据指到贴图，按 `T` 铺到地形上。
