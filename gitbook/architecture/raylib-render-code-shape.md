# Raylib 渲染装配代码形状

本页回答一个问题：想改画面，代码该去哪。按装配体（csproj）切分，谁住在哪里、依赖朝哪个方向流、每个渲染器对应哪个文件与哪条车道。逐场景的演示讲解见 [引擎画廊 Wiki](../reference/engine-gallery-wiki/README.md)；车道合同见 [渲染光照栈指南](render-lighting-guide.md)。

## 装配体分层与依赖方向

```
Ludots.Raylib.Render ──依赖──▶ Ludots.Platform.Abstractions ◀──实现── Core（registry/buffer/VFS）
        ▲                              ▲
        │纯消费合同                     │同一合同
Ludots.App.RaylibEngineGallery   Ludots.Adapter.Raylib（宿主：把 Core 服务接线进渲染器）
                                        ▲
                                 Ludots.Client.Raylib（客户端壳：输入/诊断/表现目录合并/地形 chunk 源）
```

| 装配体 | 路径 | 依赖边界 | 角色 |
|---|---|---|---|
| `Ludots.Raylib.Render` | `src/Client/Ludots.Raylib.Render` | 仅 Platform.Abstractions + Raylib-cs/SkiaSharp，**零 Ludots.Core** | 全部渲染器、shader 装载、光照总线、渲染诊断 |
| `Ludots.Platform.Abstractions` | `src/Platform/Ludots.Platform.Abstractions` | 零依赖（纯合同） | 渲染器输入合同：绘制条目、资产 DTO、动画打包状态、缓冲、服务接口 |
| `Ludots.Adapter.Raylib` | `src/Adapters/Raylib/Ludots.Adapter.Raylib` | Core + Render | 宿主适配器：装配 Core 服务实现合同接口，驱动渲染器消费 Core 表现请求 |
| `Ludots.Client.Raylib` | `src/Client/Ludots.Client.Raylib` | Core + Render | 客户端壳：输入、诊断、`PresentationCatalogMerge`、`VertexMapTerrainChunkMeshSource` 等桥接实现 |
| `Ludots.App.RaylibEngineGallery` | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery` | Render + Abstractions，**零 Core** | 引擎画廊：一能力一场景，程序化自含资产 |
| `Raylib-cs` | `src/Libraries/Raylib-cs` | — | vendored 绑定 |

方向铁律：渲染器永不 import Core；Core 只通过 Abstractions 里的合同被消费。画廊用纯数据直接实现同一合同驱动渲染器——这是「渲染器不认识宿主」的结构性证明。

## 合同层（Platform.Abstractions）速查

`src/Platform/Ludots.Platform.Abstractions/` 下与渲染相关的核心类型：

| 合同 | 文件 | 谁消费 |
|---|---|---|
| 绘制条目族 | `PrimitiveDrawItem.cs`、`ProceduralMeshAssetData.cs`、`ProjectedDecalVolume.cs` | `RaylibPrimitiveRenderer` 直接/实例化车道 |
| 蒙皮批量 | `SkinnedVisualBatchItem.cs`、`AnimatorPackedState(.Flags).cs` | `RaylibGpuSkinnedBatchRenderer` |
| 资产 DTO | `MeshAssetDescriptor.cs`、`MaterialAssetDescriptor.cs`（含 `MaterialAssetResolver` 实例链合并） | 材质库 / mesh 注册表 / 各车道 |
| 粒子 | `ParticleVfxAssetData.cs`、`ParticleSystemRuntime.cs`、`ParticleVfxSpawnMode.cs` | `RaylibVfxRenderer` |
| 覆盖层 | `GroundOverlayBuffer.cs`、`SplineRibbonBuffer.cs` | `RaylibWorldOverlayRenderer` |
| 拖尾轨迹 | `TrailMeshBuffer.cs`、`TrailSampleHistory.cs`（采样/老化共享纯工具，Core runtime 与画廊场景共用） | `RaylibTrailMeshRenderer` |
| 调试 | `DebugDrawCommandBuffer.cs` | `RaylibDebugDrawRenderer` |
| 地形 | `ITerrainChunkMeshSource.cs`、`IVisualHeightmap(RenderSource).cs`、`VisualHeightmapRenderProfile.cs` | 地形/高度图渲染器 |
| 相机/数学 | `CameraRenderState3D.cs`、`VisualMath.cs`、`LODLevel.cs` | 全部 |
| 配置载体 | `MergedConfigEntry.cs` | 环境类渲染器（天空/水体） |
| 基准直驱 | `IRaylibBenchmarkRenderer.cs` | `RaylibBenchmarkRenderer`（画廊与平台基准共用） |

## 渲染器清单（Ludots.Raylib.Render/Rendering/）

46 个文件的职责分组：

| 分组 | 文件 | 车道 |
|---|---|---|
| 图元合批 | `RaylibPrimitiveRenderer`、`RaylibInstancedMaterialPipeline`、`RaylibMaterialDrawState`、`RaylibIsmRenderBridge`、`StaticMeshAdapterSyncPlanner` | `instancing`（ISM） |
| 蒙皮合批 | `RaylibGpuSkinnedBatchRenderer`、`RaylibGpuSkinnedModelCache`、`RaylibSkinnedPlayback` | `skinning_instanced` |
| 单物体光照 | `RaylibLitModel`、`RaylibFrameLighting`、`RaylibSkyIbl`、`RaylibDirectionalShadowMap`、`RaylibShadowSampling` | `model_lit` + 深度 pass 族 |
| 环境 | `RaylibSkyboxRenderer`、`RaylibSkyEnvironment`、`RaylibRenderEnvironment(Renderer/Config)`、`RaylibWaterPass`、`RaylibPostProcessRenderer` | `skybox` / `sky_daynight` / `water` / `postprocess` |
| 地表 | `RaylibTerrainRenderer`、`RaylibVisualHeightmapRenderer`、`RaylibVegetationCutoutRenderer`、`RaylibDecalProjectorRenderer`、`IRaylibReceiverMeshProjector` | `terrain` / `vegetation_cutout` / `decal_project` |
| 材质/着色 | `RaylibMaterialLibrary`、`RaylibShaderCatalog`、`RaylibLaneShader`、`RaylibShaderLoader`、`RaylibShaderBindingGuard`、`RaylibEffectShaderRegistry` | 装订与分派 |
| 特效/覆盖 | `RaylibVfxRenderer`、`RaylibWorldOverlayRenderer`、`RaylibTrailMeshRenderer`、`TrailMeshGeometry`、`RaylibSkiaRenderer` + `SkiaRasterLayer` | `vfx_unlit_tint` / overlay / trail-mesh / Skia |
| 工具 | `RaylibDebugDrawRenderer`、`RaylibBenchmarkRenderer`、`RaylibColorUtil`、`RenderDiagnostics` | 调试 / 基准 / 诊断出口 |

shader 装载约定唯一真源在 `src/Platforms/Desktop/`（经 csproj 传递复制到输出根），include 展开与 fail-loud 校验由 `RaylibShaderLoader` 运行时执行。

## 着色器清单（src/Platforms/Desktop/）

15 组 `.vs/.fs` + 2 个共享 include：

| 着色器 | 车道 | 备注 |
|---|---|---|
| `model_lit` | 单物体 GGX | 接收阴影 |
| `instancing` | ISM 合批 | 接收阴影 |
| `skinning_instanced` | 蒙皮合批 | 接收阴影 |
| `terrain` | 地表/高度图 | 接收阴影 |
| `vegetation_cutout` | 植被 billboard | 接收阴影（alpha 打孔） |
| `skybox` / `sky_daynight` | 天空两形态 | 共享 `sun_disk.glsl.inc` |
| `water` | 水面 | 采样反射/折射双 RT |
| `decal_project` | 投影贴花 | 沿世界 Y 投影 |
| `postprocess` | 后处理调色 | 曝光/对比/饱和/暗角 |
| `vfx_unlit_tint` | 粒子 | 无光照着色 |
| `shadow_depth`（+`_cutout` / `_instanced` / `_skinning_instanced`） | 深度 pass 族 | 四接收 shader 经 `shadow_sampling.glsl.inc` 共享采样块 |
| 自定义（如画廊 `mat_emissive`） | 经 `RaylibShaderCatalog` 注册的实例化车道 | 非实例化车道遇非默认 key fail-loud |

## 帧内数据流（宿主路径）

1. Core `PresentationRequest`（Mesh / Decal / VFX / Surface / GroundOverlay / SplineRibbon / HUD）→ `PresentationRequestFlushSystem` 写 `PrimitiveDrawBuffer` 等缓冲（见 [产品化合同](raylib-render-productization.md)）。
2. 唯一帧执行者 `RaylibFrameRenderer`（#1323 起）：先 `BuildPassPlan` 声明本帧 pass 再逐项执行，声明顺序=执行顺序。顺序为 Clear → 水面反射/折射（如启用）→ `ShadowDepth`（`RaylibDirectionalShadowMap.BeginFrame` → 地形/高度图/基准场景/即时/蒙皮/车道灌深度 → `EndFrame`，由 `RenderDebugState.DrawShadows` 门控；阴影盒以相机目标为中心、`LUDOTS_RAYLIB_SHADOW_SCENE_RADIUS`（默认 48m）为场景半径，盒外接收面判定无阴影）→ 后处理 RT → 世界 3D 序列 → 后处理合成 → UI 层。后处理 RT 在水面 pass 之后开启——水面 `EndTextureMode` 会切回默认帧缓冲，先开后处理再画水面会丢 RT 绑定（旧版水面帧直接熄掉调色的根因，已修）。
3. 各渲染器消费合同缓冲出画；`RaylibPostProcessRenderer`（或 Skia 合成）收尾。`RaylibHostLoop` 只构建一帧输入记录并调用一次 `RenderFrame`，`EndDrawing`/诊断 HUD/截图取证留在宿主。

画廊路径是同构缩小版：场景类手工填 `GalleryPrimitiveSnapshot`（合同缓冲的画廊等价物）直接喂渲染器，验证渲染器对宿主零知识。

## 资产加载错误策略合同（#1326 定稿）

- 合同：宿主资产装载失败一律 fail-loud（抛出带资产 id 与原因的异常），不走"警告+跳过绘制"的静默降级；材质/蒙皮车道现行行为即标准。
- 现状偏差（待 #1327 收敛）：mesh/billboard 模型与贴图在 `RaylibPrimitiveRenderer` 懒加载路径是 warn-once + 跳过；Sound 装载失败在 `RaylibSoundConsumer` 记警告并跳过，且 `_failedAssetIds` 形成进程内永久负缓存；部分 instanced material 装配错误同样是 warn-and-skip。当前所有负缓存均不可失效，重试语义随 #1327 落地。
- 负缓存失效：装载失败不得形成永久负缓存——同一资产在来源变化（文件补齐/内容版本更新）后必须可重试，失败原因与重试次数可诊断；实现随 #1327 落地。

## 配置与资产入口

作者面配置文件（`material_assets.json` / `host_assets.json` / 环境配置树 / presets）的结构与装载规则见 [Raylib 渲染配置结构](../reference/raylib-render-config-structure.md)。
