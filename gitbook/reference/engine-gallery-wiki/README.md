# Raylib 引擎画廊 Wiki

每个引擎渲染场景一页：一场页内可播的验收录像（真实运行采样拼制，非特效）加一节作者写法看清入口。场景容器由 `projects/engine_gallery/` 下的 `<scene id>.scene.json` 提供，`catalog.json` 只登记容器资产；门户登记走 `showcase.registry.json`（每场景一条 `engine_raylib_<scene id>` 条目，title/summary 与关卡容器逐字一致）；录像由 `scripts/record-engine-galleries.py` 生成于 `artifacts/evidence/engine_raylib_<scene id>/`，截图与帧统计摘自 `artifacts/acceptance/engine_gallery_all/`（120 帧验收批）与 `artifacts/acceptance/engine_raylib_lighting/`、`artifacts/acceptance/engine_raylib_crowd_anim/`（重场景独立批）。目录由 `scripts/build-site.py` 解析本页生成侧栏导航，并校验链接页面存在。

整个画廊一条命令浏览菜单，单场景加 preset 名直达（见每页「怎么跑」）。分层边界（引擎画廊 / 平台基准 / 表现系统 showcase）见 [Raylib 引擎能力标准化 Showcase](../../architecture/engine-capability-showcases.md)；改代码前先看 [渲染装配代码形状](../../architecture/raylib-render-code-shape.md) 与 [渲染配置结构](../raylib-render-config-structure.md)；给画廊加新场景/新着色器的登记环见 [引擎画廊开发指南](../../architecture/raylib-engine-gallery-dev-guide.md)。

## 光照与大气

- [一排球把整条光照栈亮给你看](lighting.md) — GGX 粗糙度×金属度梯度球阵 + 环绕太阳 + split-sum IBL + 深度阴影。
- [太阳从早走到晚，物体当场变色](frame_lighting.md) — 光照总线相位摆动，日光/环境/阴影即时联动。
- [四十八秒过完一整天](sky_daynight.md) — 渐变烘焙天空 + 全天相位驱动。
- [头顶的天空是画出来的渐变](skybox.md) — 程序化渐变天空盒 + 太阳方位绕行。
- [二十排方碑走进雾里](atmosphere_fog.md) — 距离雾衰减 + 环境色调接管地平线。
- [四根调色推子一起推](postprocess.md) — 曝光/对比/饱和/暗角随时间调制。

## 地形与水体

- [一座岛的皮肤是顶点色刷的](terrain_surface.md) — chunk 网格高地分带着色 + 湖面水体。
- [海拔越高颜色越浅的岛屿](terrain_heightmap.md) — 绝对海拔色带 + 水下陆架。
- [水面上下各画一遍，合成一片海](water.md) — 反射/折射双通道 + DUDV 扭曲。

## 材质与地表细节

- [八块立方，八种材质合同](material_binding.md) — 多材质/混合模式 + 实例链 + shaderKey 自定义着色。
- [草和树是贴片，影子会漏光](vegetation_cutout.md) — alpha-cutout 公告板植被 + 打孔阴影。
- [三枚标记在地形上巡游](decal_projection.md) — 沿世界 Y 投影到起伏地表的贴花。

## 大批量与动画

- [三万个方块球，一次合批画完](instancing.md) — 30k 纯数据实例 ISM 吞吐。
- [十二具骨架，各自走路](gpu_skinning.md) — 逐实例非合批 GPU 蒙皮。
- [四千个兵环形行军](crowd_anim.md) — 真 GPU 蒙皮实例化合批，CPU 只打包动画相位。
- [图元阵与原型的动效基线](primitives.md) — 直接模式图元 + AnimatorPackedState 驱动动效。

## 覆盖层与 HUD

- [火花、烟雾、火星拖尾](particles.md) — Quarks 粒子三组效果。
- [地面上的圈与飘带](ribbon_overlay.md) — 环形/扇形地面覆盖 + 样条带。
- [挥砍的刀光弧线](slash_trail.md) — TrailMeshBuffer 武器弧形 mesh 拖尾 + 顶点色渐隐。
- [关卡容器的组合实拍](composition.md) — 岛屿 + 36 实例静态网格材质链 + 双 guard 动画，纯 JSON 多节点组合。
- [3D 画面上贴一块 2D 仪表盘](skia_overlay.md) — Skia GPU 2D 覆盖层合成。

## 调试

- [给世界画辅助线](debug_draw.md) — 线/圆/盒/视锥命令缓冲消费。
