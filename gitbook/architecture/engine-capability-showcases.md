# Raylib 引擎能力标准化 Showcase

## 定位与分层

Raylib 相关 showcase 分三层，层间以依赖方向区分，不得越层引用：

| 层 | 载体 | 依赖边界 | 覆盖内容 |
|---|---|---|---|
| **engine 引擎能力** | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery`（独立可执行画廊，`--scene <id>` 一能力一场景） | 仅 `Ludots.Raylib.Render` + `Ludots.Platform.Abstractions` + Raylib-cs/SkiaSharp，**零 Ludots.Core** | 下表 21 项引擎渲染能力 |
| **platform-benchmark 平台基准** | `raylib_client_parity` / `raylib_ism_benchmark`（宿主内纯数据驱动 mod） | Ludots.Core + `IRaylibBenchmarkRenderer` 直驱，绕过 Presenter/实体管线 | 宿主装配下的平台渲染开销（ISM 吞吐、蒙皮人群基线） |
| **presentation 系统展示** | `raylib_visual_atmosphere` / `vfx_forge_raylib` / `presenter_blacksmith` 全家等 | 完整 Presentation 请求链路（Presenter → 请求通道 → Raylib 消费） | 表现系统合同、资产驱动、HUD/Presenter 行为 |

明确排除在引擎画廊外的（属 Ludots Presentation 系统能力）：field overlay 战争迷野、HUD/Presenter 链路、minimap。

## 21 项引擎渲染能力目录（画廊场景清单）

逐场景的演示讲解（验收截图 + 作者写法 + 怎么跑）见 [引擎画廊 Wiki](../reference/engine-gallery-wiki/README.md)；下表是能力矩阵与承接渲染器。

| # | scene id | 能力 | 承接渲染器 |
|---|---|---|---|
| 1 | skybox | 天空盒 | RaylibSkyboxRenderer |
| 2 | sky_daynight | 昼夜天空/日照 | RaylibSkyEnvironment + sky_daynight |
| 3 | water | 水面（反射/折射/DUDV） | RaylibWaterPass |
| 4 | terrain_surface | 地表着色（hex chunk 网格） | RaylibTerrainRenderer + ITerrainChunkMeshSource |
| 5 | terrain_heightmap | 视觉高度图（色带/降采样） | RaylibContinuousHeightmapRenderer + IContinuousHeightmapRenderSource |
| 6 | atmosphere_fog | 距离雾 + 环境色 ramp | RaylibRenderEnvironmentConfig/Renderer |
| 7 | frame_lighting | 帧光照 | RaylibFrameLighting |
| 8 | postprocess | 后处理调色 | RaylibPostProcessRenderer |
| 9 | gpu_skinning | GPU 骨骼蒙皮 | RaylibGpuSkinnedModelCache + RaylibSkinnedPlayback |
| 10 | instancing | GPU 实例化合批 ISM | RaylibBenchmarkRenderer（IRaylibBenchmarkScene 纯数据） |
| 11 | particles | Quarks 粒子 | ParticleVfxAssetData + RaylibVfxRenderer |
| 12 | decal_projection | 投影贴花 | decal_project + IRaylibReceiverMeshProjector |
| 13 | vegetation_cutout | 植被透贴 | vegetation_cutout |
| 14 | material_binding | 材质绑定 | RaylibMaterialHostBinder |
| 15 | ribbon_overlay | 样条带/地面覆盖 | RaylibWorldOverlayRenderer（绘制核唯一实现，宿主与画廊共用） |
| 16 | skia_overlay | Skia GPU 2D 覆盖层 | RaylibSkiaRenderer + SkiaRasterLayer |
| 17 | debug_draw | 调试绘制 | RaylibDebugDrawRenderer + DebugDrawCommandBuffer |
| 18 | primitives | 图元/群体渲染与群体动画 | RaylibPrimitiveRenderer |
| 19 | lighting | 光照全效（GGX 梯度/split-sum 天空 IBL/深度阴影） | RaylibLitModel + RaylibSkyIbl + RaylibDirectionalShadowMap |
| 20 | crowd_anim | 大量动画实例合批 | skinning_instanced 真骨骼 GPU 蒙皮 × 4k 实例 |
| 21 | slash_trail | 刀光轨迹（TrailMeshBuffer 弧形拖尾） | RaylibTrailMeshRenderer + TrailMeshGeometry |

画廊实拍选粹（既有 20 场景截图见 Wiki 各场景页与 `artifacts/acceptance/engine_gallery_all/`；新增 `slash_trail` 的视觉证据待真实运行采样后补齐，见 [slash_trail Wiki 页](../reference/engine-gallery-wiki/slash_trail.md)）：

<img src="artifacts/acceptance/engine_gallery_all/instancing.png" alt="GPU 实例化合批验收截图" width="560"> <img src="artifacts/acceptance/engine_gallery_all/terrain_heightmap.png" alt="视觉高度图验收截图" width="560"> <img src="artifacts/acceptance/engine_gallery_all/sky_daynight.png" alt="昼夜天空验收截图" width="560">

## 标准化合同

1. **场景代码**：`Scenes/<Id>Scene.cs` 实现 `IEngineScene { Id, Title, Summary, Load, Draw, Dispose }`；自含可读、数据程序化生成；`SceneCatalog` 显式注册；画廊菜单自动枚举。
2. **验收 CLI**：`--scene <id> --screenshot <path> --frames N --json <stats>`；截图 + 帧统计（avg/p95/max）为每场景标准证据。
3. **注册表**：每场景一条 `engine_raylib_<id>` 条目（category=engine、tier 与四件套按验收状态补齐）。
4. **资产**：画廊 assets 自持最小 fixture（模型 GLB、粒子 JSON、程序化数据），不引用 mods/、不依赖宿主。

## 架构支撑

- `Ludots.Raylib.Render`（`src/Client/Ludots.Raylib.Render`）：零 Core 渲染程序集，20 项渲染器全部居住于此；shader 装载约定唯一真源（`src/Platforms/Desktop` 经 csproj 传递复制）。
- `Ludots.Platform.Abstractions`：渲染器输入合同（PrimitiveDrawItem 族、资产 DTO、动画打包状态、相机状态、地形/快照接口、IRenderMeshAssets 等服务合同、MergedConfigEntry、VisualMath）。
- Core 侧 registry/buffer/VFS 实现合同接口；宿主（Adapter.Raylib）负责把 Core 服务接线进渲染器；引擎画廊以纯数据实现同一合同直接驱动渲染器。
- 旧独立工具 `gpu_skinned_instance_probe` / `raylib_client_parity_acceptance` 已退役，能力由画廊场景接管。

## 启动

```bash
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibEngineGallery                       # 菜单浏览
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibEngineGallery -- --scene sky_daynight
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibEngineGallery -- --scene instancing --frames 300 \
  --screenshot artifacts/acceptance/engine_raylib_instancing/screen.png \
  --json artifacts/acceptance/engine_raylib_instancing/stats.json
```
