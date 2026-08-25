# Raylib 渲染配置结构

Raylib 桌面端的全部作者面配置文件：每个文件长什么样、字段语义、装载规则与 fail-loud 边界。合同层类型见 [渲染装配代码形状](../architecture/raylib-render-code-shape.md)；光照/材质合同细节见 [渲染光照栈指南](../architecture/render-lighting-guide.md)。

所有 mod 侧文件走 ConfigPipeline 的 `ArrayById` 合并（同 id 多 fragment 按装载序合并，冲突进报告）；跨 mod 资产引用用虚拟 URI `"ModName:assets/…"`。装载错误一律抛出终止，没有静默默认。

五类作者面文件均有 JSON Schema（`assets/Presentation/*.schema.json`，字段集镜像各 ConfigLoader 的 allow-list 并经全仓 78 个配置文件实测零误报，`ludots://presentation/…`，随资产分发供编辑器/工具做结构提示）——**schema 不参与流水线校验**，装载期 fail-loud 合同以各 ConfigLoader 为准（与 `assets/AI/*.schema.json` 同约定）。

## Presentation/material_assets.json — 材质

装载器：`src/Core/Presentation/Config/PresentationMaterialConfigLoader.cs`（注册进 `PresentationMaterialRegistry`，经 `MaterialAssetResolver` 沿 `parent` 链合并出解析后视图）。Schema：`assets/Presentation/material_assets.schema.json`。

```json
[
  { "id": "stone.wall", "domain": "Surface" },
  { "id": "stone.wall.rusty", "domain": "Surface", "parent": "stone.wall",
    "roughness": 0.95, "params": { "colors": { "uEmissiveColor": [0.2, 0.9, 1.0, 1.0] } } },
  { "id": "iron.glow", "domain": "Surface", "shaderKey": "emissive", "flags": ["Cutout"],
    "roughness": 0.4, "metalness": 0.8,
    "params": { "floats": { "uEmissiveStrength": 3.0 } } }
]
```

| 字段 | 语义 | 校验 |
|---|---|---|
| `id` | 材质 key（合并锚） | 必填、非空、无首尾空白 |
| `domain` | 资产域（如 `Surface`） | 必填、大小写敏感枚举 |
| `parent` | 实例链父 key；子材质只写差异字段 | **实例不得声明 `shaderKey`/`flags`** |
| `shaderKey` | 自定义着色行为，分派到注册的实例化车道 | 默认 `lit`；非实例化车道遇非默认值 fail-loud |
| `flags` | 混合模式数组（如 `["Cutout"]`、`["AlphaBlend"]`；`"Opaque"` 显式无害） | 必须是数组；未知值抛出 |
| `roughness` / `metalness` | 标量 PBR 直推 uniform | 数值且 ∈ [0,1]；与 `params.floats.roughness` 二写即抛 |
| `params.floats` | 命名标量 → shader uniform | name→number |
| `params.colors` | 命名颜色 → shader uniform | name→[r,g,b,a] 四元数组 |

贴图**不在这里**：本文件声明 `sourceUris`/`textures` 直接抛出——平台资源路径属于 `host_assets.json`。有贴图槽位时贴图优先、标量忽略（见光照指南「材质标量 PBR 合同」）。

## Presentation/host_assets.json — 平台资产装订

把平台无关的资产 key 绑到 raylib 后端的实际来源。两种行（Schema：`assets/Presentation/host_assets.schema.json`）：

```json
[
  { "id": "demo.soldier.raylib", "assetKind": "Mesh", "assetId": "demo.soldier",
    "backendId": "raylib", "sourceUris": ["DemoMod:assets/Models/soldier.glb"] },
  { "id": "demo.palm.material.raylib", "assetKind": "Material", "assetId": "demo.palm",
    "backendId": "raylib", "textures": { "albedo": "DemoMod:assets/Textures/palm.png" } }
]
```

- **Mesh 行**：`sourceUris` 指向 GLB/OBJ（mod 虚拟 URI）。
- **Material 行**：`textures` 按槽位名（`albedo`/`roughness`/`metallic`/`normal`）绑贴图 URI；渲染期解析不到时 `RenderDiagnostics.Warn` warn-once + 跳过条目，不画占位体。

活样例：`mods/capabilities/navigation/MassNavigationMod/assets/Presentation/host_assets.json`（Mesh 行）、`mods/showcases/raylib_visual_atmosphere/RaylibVisualAtmosphereShowcaseMod/assets/Presentation/host_assets.json`（Material 行 ×9）。

## Presentation/mesh_assets.json — mesh 句柄

装载器：`src/Core/Presentation/Config/MeshAssetConfigLoader.cs`（Schema：`assets/Presentation/mesh_assets.schema.json`）。

```json
[
  { "id": "cube", "type": "Primitive", "primitiveKind": "Cube" },
  { "id": "demo.soldier", "type": "Model" }
]
```

- `type: "Primitive"` + `primitiveKind`（`Cube`/`Sphere`/…）程序化图元；
- `type: "Model"` 指模型文件；`type: "Billboard"` 挂贴图做公告板（植被车道）；
- VFX 句柄：`vfx.particleVfxId` 引用粒子定义（见 [产品化合同](../architecture/raylib-render-productization.md)）；
- 本文件同样禁止 `sourceUris`（平台路径归 host_assets）。

## Presentation/particle_vfx.json — Quarks 粒子

Quarks schema 的作者面（发射率、生命周期、尺寸区间、`spawnMode` 等），全量字段见 [Quarks Particle Schema](../architecture/quarks-particle-schema.md)（JSON Schema：`assets/Presentation/particle_vfx.schema.json`）。画廊 `particles` 场景是三组效果的活样例（加色火花/贴图烟雾/拉伸火星）。

## Presentation/presenters.json — 定义与出生规则

定义（组合树：behaviors/children/paramDefaults）与规则（event × condition → command）的完整字段合同见 [Presenter-as-Actor 架构设计](../architecture/presenter-as-actor-architecture.md)与[快速上手](../architecture/presenter-quickstart.md)（JSON Schema：`assets/Presentation/presenters.schema.json`，覆盖 13 种 BehaviorKind、11 种 PresenterCommandKind、36 种 PresentationEventKind，枚举源 `src/Core/Presentation/Presenters/BehaviorSlot.cs`、`src/Core/Presentation/Presenters/PresenterCommandKind.cs`、`src/Platform/Ludots.Platform.Abstractions/PresentationEventKind.cs`）。

## 环境配置树 — 光照/雾/天空/阴影

| 文件 | 内容 | 消费方 |
|---|---|---|
| `distance_fog.json` | `{enabled, density, start, end, color:[r,g,b]}` | `RaylibFrameLighting`（雾四参数） |
| `ambient_day_ramp.json` | `lightColor`/`lightIntensity` + 11 站 `samples[]`（phase→rgb+intensity） | 环境光昼夜 ramp |
| 天空/水体 MergedConfigEntry | `backendId`/`enabled`/`mapIds`/`gradientStops`（天空）或 `waterPlaneY`/`resolutionScale`/`waveStrength`/`moveSpeed`/`dudvUri`（水体） | `RaylibSkyEnvironment` / `RaylibWaterPass` |
| `RaylibShadowConfig` | `MapSize`（默认 2048）、`ReceiverBiasWorld`（默认 0.04） | `RaylibDirectionalShadowMap` |

`distance_fog.json` 与 `ambient_day_ramp.json` 的默认件在 `src/Client/Ludots.Raylib.Render/Resources/`（csproj 复制到输出根，`RaylibFrameLighting.LoadFromDefaultPath` 从 `AppContext.BaseDirectory` 装载）；mod 用同结构文件覆盖。画廊里 sky_daynight / water 场景就是手工 `MergedConfigEntry` 演示这两个条目的作者写法。

## launcher.presets.json — 引擎画廊 preset

每场景一条（20 条，selectors `["$engine_gallery"]`）：

```json
{ "id": "engine_raylib_water", "name": "引擎画廊·water", "selectors": ["$engine_gallery"],
  "adapterId": "raylib",
  "args": ["--scene", "water", "--frames", "120",
           "--screenshot", "artifacts/acceptance/engine_raylib_water/screen.png",
           "--json", "artifacts/acceptance/engine_raylib_water/stats.json"] }
```

`--scene/--frames/--screenshot/--json` 是画廊 CLI 的验收合同（截图 + 帧统计为标准证据）；重场景（lighting/crowd_anim）走独立批目录，其余 18 场景由 CI 批跑汇入 `artifacts/acceptance/engine_gallery_all/`。新增场景的完整登记环见 [引擎画廊开发指南](../architecture/raylib-engine-gallery-dev-guide.md)。
