# Raylib 最小引擎能力总览

Ludots 的 Raylib 桌面引擎适配器（`src/Client/Ludots.Raylib.Render` + `src/Client/Ludots.Client.Raylib`）是一套**最小但完整**的商业引擎式渲染基建：车道化渲染、材质实例、方向光阴影、split-sum IBL、数据驱动昼夜。全部作者面走 JSON 配置与注册表，合同 fail-loud，无静默降级、无 fallback 分支。

## 渲染车道矩阵

| 车道 | 用途 | shader | 入口 |
|---|---|---|---|
| 静态实例化合批（ISM） | 大量静态网格（植被、道具群） | `instancing.vs/fs` | `PrimitiveDrawItem` → `RaylibPrimitiveRenderer` |
| GPU 骨骼蒙皮实例化 | 人群/军队同模型动画 | `skinning_instanced` | `ISkinnedVisualBatchSnapshot` → `RaylibGpuSkinnedBatchRenderer` |
| 单物体带光照 | 少量模型、编辑器道具 | `model_lit` | `RaylibLitModel` |
| 地形 | 高度图 / 平面 surface | `terrain.fs` | `RaylibVisualHeightmapRenderer` / `RaylibTerrainRenderer` |
| 植被 billboard | 镂空贴图植物 | `vegetation_cutout` | `MeshAssetType.Billboard` + Cutout 材质 |
| 投影贴花 | 脚印、弹坑、标记 | `decal_project` | `AssetKind.Decal` → `RaylibDecalProjectorRenderer` |
| 天空 | 昼夜渐变 / 程序化天空盒 | `sky_daynight` / `skybox` | `RaylibSkyEnvironment` / `RaylibSkyboxRenderer` |
| 其他 | 水体 / 后处理 / 调试绘制 / ribbon / Skia 覆盖层 / 粒子 VFX | `water` / `postprocess` / … | 各专用渲染器 |

两条主光照车道（合批 + 单物体）合同词汇一致：**Cook-Torrance GGX 单灯 metallic-roughness + split-sum 天空 IBL**，光照总线 `RaylibFrameLighting`（光向/环境/光色/强度/雾/视点）。

## 材质系统

三轴正交、全部数据驱动（`Presentation/materials.json` + `MaterialAssetDescriptor`）：

- **换贴图 / 改参数**：材质实例链 `ParentKey`，子材质只写差异字段，`MaterialAssetResolver` 沿链合并；
- **改着色行为**：`ShaderKey` + `RaylibShaderCatalog` 注册表分派到实例化车道；非实例化车道遇自定义 key fail-loud；
- **命名参数**：`FloatParams`/`ColorParams` 按键名直推 shader uniform；
- **标量 PBR**：无贴图材质 `roughness`/`metalness` 直达 `uRoughness`/`uMetallic`；贴图优先。

宿主侧统一材质装订库 `RaylibMaterialLibrary` 经 `IRenderMaterialAssets.TryResolve` 消费解析后视图，重注册不丢标量参数。

## 光照与 IBL

- **方向光 + 环境**：`RaylibFrameLighting` 从环境配置装载，昼夜相位驱动；
- **split-sum 天空 IBL**：`RaylibSkyIbl` CPU 烘焙 6×64² 环境立方图（mip 链按粗糙度 GGX 预滤波）+ 512² BRDF LUT（Hammersley 256 采样数值积分），两车道 `ambientSpecular` 同合同；相位步进节流重烘，GPU 端零额外 pass；
- **雾与天空**：`distance_fog.json`、天空环境 JSON、环境光 ramp，全配置文件驱动。

## 方向光阴影

`RaylibDirectionalShadowMap`：深度打包 RGB24 颜色 RT + 3×3 PCF 接收；四接收 shader 经 `// ludo:include shadow_sampling.glsl.inc` 共享唯一采样源（`RaylibShaderLoader` 运行时展开，禁内联 + 逐字一致双合同）。

投影资格矩阵：

| 材质 blend | 投影 | 形态 |
|---|---|---|
| Opaque | ✓ | 实体深度 |
| Cutout | ✓ | alpha 打孔（树冠影斑驳，非实心矩形） |
| AlphaBlend / Additive | ✗ | 覆盖/发光语义不构成遮挡体 |
| VFX / Decal | ✗ | 条目级跳过 |

阴影参数走环境配置树 `RaylibShadowConfig(MapSize, ReceiverBiasWorld)`，无魔法数硬编码。

## 配置与资产

- 资产注册：`host_assets.json`（mesh/材质/贴图行）→ `RaylibMaterialLibrary` / mesh 注册表；
- 环境配置：天空环境、雾、阴影、环境光 ramp 各自 JSON，装载期严格校验；
- 太阳盘/光晕四参数（`RaylibSkyboxConfig`）双天空 shader 共享 `sun_disk.glsl.inc`。

## 引擎画廊：20 场景一键验收

```powershell
.\scripts\run-mod-launcher.cmd cli launch preset:engine_raylib_lighting --adapter raylib
```

| preset | 演示点 |
|---|---|
| `engine_raylib_primitives` | 图元车道基线 |
| `engine_raylib_instancing` | 静态实例化合批 |
| `engine_raylib_gpu_skinning` | GPU 蒙皮实例化 |
| `engine_raylib_crowd_anim` | 4096 动画实例人群合批 |
| `engine_raylib_lighting` | 光照全效：GGX 粗糙度×金属度梯度球阵 + IBL + 阴影 |
| `engine_raylib_frame_lighting` | 光照总线昼夜 |
| `engine_raylib_material_binding` | 材质库/实例链/shaderKey 自发光 |
| `engine_raylib_vegetation_cutout` | 镂空植被 + alpha 打孔影 |
| `engine_raylib_decal_projection` | 地形投影贴花 |
| `engine_raylib_terrain_surface` / `engine_raylib_terrain_heightmap` | 地形两车道 |
| `engine_raylib_skybox` / `engine_raylib_sky_daynight` | 天空两车道 |
| `engine_raylib_water` / `engine_raylib_atmosphere_fog` / `engine_raylib_postprocess` | 水体/雾/后处理 |
| `engine_raylib_particles` / `engine_raylib_ribbon_overlay` / `engine_raylib_skia_overlay` / `engine_raylib_debug_draw` | 粒子/ribbon/Skia/调试绘制 |

每场景有验收六件套证据（截图 + stats），见「测试与验收」页。

## 质量门

- 合同测试锁 shader 接线（`RaylibShaderContractTests`：深度打包一致性、接收端采样块逐字一致、镂空/投影资格）；
- 场景截图回归基线（静态场景像素级比对）；
- 新增 shader 强制走 `RaylibShaderLoader` include 展开与 fail-loud uniform 校验。

## 深读

- [渲染光照栈与下游使用指南](render-lighting-guide.md)——车道接线、IBL 实现细节、材质合同；
- [Raylib 引擎能力标准化 Showcase](engine-capability-showcases.md)——能力矩阵与验收登记。
