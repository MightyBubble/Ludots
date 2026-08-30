# Raylib 引擎画廊开发指南

三个最常见的开发环：给画廊加一个场景、给渲染栈加一个着色器、给 mod 加一种材质。每环列全量登记点——漏一处不是"少个文档"，是 CI/构建期直接拦下。装配体边界与渲染器清单见[渲染装配代码形状](raylib-render-code-shape.md)；配置文件字段见[渲染配置结构](../reference/raylib-render-config-structure.md)。

## 环一：加一个画廊场景（六处登记）

以现有场景 `vegetation_cutout` 为走查样本（新场景照抄六处换名）：

1. **场景类**：`src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Scenes/VegetationCutoutScene.cs` 实现 `IEngineScene { Id, Title, Summary, Load, Draw, Dispose }`；自含可读、数据程序化生成，不引用 mods/、不依赖宿主（零 Core 是画廊的分层合同）。
2. **目录注册**：`SceneCatalog.cs` 的 `Entries` 数组显注册一行（id/标题/摘要/factory）——画廊菜单自动枚举。
3. **preset**：`launcher.presets.json` 加 `engine_raylib_<id>` 条目（`--scene <id> --frames 120 --screenshot … --json …`，selectors `["$engine_gallery"]`）。
4. **注册表**：`showcase.registry.json` 加条目：`category: "engine"`、`binding: "engine_gallery"`、`preset`、`acceptanceTest: "RaylibEngineGalleryTests"`、`artifactDir`、`screenshot`、`docsPath` 指回本文档族；随后跑 `python scripts/build-acceptance-index.py` 同步 `scripts/acceptance/acceptance.index.json`（CI 用 `--check` 校验同步，忘跑即红）。
5. **验收证据**：本地跑一次 preset 落截图 + stats（命令见下）；CI 的 `ci-acceptance.yml` 会按索引逐条 `--record` 重跑并门禁。
6. **Wiki 页**：`gitbook/reference/engine-gallery-wiki/` 下新增与场景 id 同名的 md 页，README 总目录照既有行格式加一行（人话标题 — 场景页链接 — 一句话简介，参考 vegetation_cutout 条目）；`scripts/build-site.py` 解析 README 生成侧栏导航，条目缺页**硬失败**、孤儿页告警。
7. **录像**：`python scripts/record-engine-galleries.py`（或 `--scene <id>` 单场景）重录页内播放的 `play.mp4` + `poster.png` 到 `artifacts/evidence/engine_raylib_<id>/`——真实运行采样拼制，录像不是可选项，Wiki 页正文嵌的就是它。

本地验收命令（产物即证据，preset 名替换为新场景）：

```text
scripts/run-mod-launcher.cmd cli launch preset:engine_raylib_vegetation_cutout --adapter raylib
```

或直接驱动 CLI：

```text
dotnet run --project src/Apps/Raylib/Ludots.App.RaylibEngineGallery -- --scene vegetation_cutout --frames 120 --screenshot artifacts/acceptance/engine_raylib_vegetation_cutout/screen.png --json artifacts/acceptance/engine_raylib_vegetation_cutout/stats.json
```

## 环二：加一个着色器（五处登记 + 三条铁律）

1. **shader 文件**：`.vs`/`.fs` 放 `src/Platforms/Desktop/`（经 csproj 传递复制到输出根——shader 装载约定唯一真源）。
2. **共享块**：需要采样/太阳盘等公共代码时，用 `// ludo:include shadow_sampling.glsl.inc` 形式的 include，**禁止复制粘贴内联**；`RaylibShaderLoader` 运行时展开（递归 ≤4、fail-loud、防路径穿越），合同测试断言"禁内联 + 展开后逐字一致"。
3. **车道注册**：实例化合批车道经 `RaylibShaderCatalog.RegisterInstancing(shaderKey, RaylibLaneShader.LoadInstancing(baseDir, vs, fs, label))`；默认 key 是 `RaylibShaderKeys.Lit`（即材质 `shaderKey` 缺省值 `lit`）。非实例化车道遇非默认 shaderKey 一律 fail-loud，不做静默降级。
4. **合同测试**：`src/Tests/RaylibAdapterTests/RaylibShaderContractTests.cs`（深度打包一致性、接收端采样块逐字一致、镂空/投影资格）与 `RaylibShaderCatalogTests.cs`（注册表语义）——新 shader 必须进合同矩阵。
5. **uniform 校验**：装载期 fail-loud 校验 uniform 存在性（`RaylibShaderBindingGuard`）；缺 uniform 直接抛，不带默认值过关。

三条铁律（native raylib 5.0 已知限制，绕法内建）：

- 矩阵不进 uniform（列用 vec3 语义）；
- 模型 mesh 不按值过边界（模型路径一律 `DrawModelEx`）；
- 可选字段默认值是合同的一部分，改动必须配合同测试守卫，不允许顺手改静默通过。

## 环三：加一种材质（两处登记，零代码）

1. `Presentation/material_assets.json` 加材质行（或 `parent` 实例链行只写差异字段）；标量 PBR、`params.floats/colors`、`shaderKey` 的字段表见[渲染配置结构](../reference/raylib-render-config-structure.md)。
2. 需要贴图时在 `Presentation/host_assets.json` 加 Material 行绑 `textures.albedo` 等槽位（mod 虚拟 URI）。

装载期校验 fail-loud（越界标量、非法 flags、实例声明 shaderKey 等直接抛）；渲染期资产缺失走 `RenderDiagnostics.Warn` warn-once + 跳过，不画占位体。活样例：画廊 `material_binding` 场景（`iron → rusty` 实例链 + `emissive → hot` 参数覆盖）。

## 何时该动哪里（决策表）

| 想要的效果 | 动哪里 | 不该动哪里 |
|---|---|---|
| 换贴图/改粗糙度/改自发光参数 | `material_assets.json` 实例行 | 任何代码 |
| 新着色行为（新光照模型/风格化） | 新 shader + `RaylibShaderCatalog` 注册 | 在现有 shader 里加开关 |
| 新物体形态（新几何来源） | `mesh_assets.json` / host_assets Mesh 行 | 渲染器 |
| 新渲染能力（新车道） | `Ludots.Raylib.Render` 新渲染器 + 画廊场景 + 本文环一 | Core、Adapter |
| 大地图远景表现 | `ContinuousHeightmapRenderProfile`（海平面/夸张/对比度） | 逐 chunk 手调 |

## 深读

- [渲染装配代码形状](raylib-render-code-shape.md)——装配体边界与渲染器/着色器全清单；
- [渲染光照栈与下游使用指南](render-lighting-guide.md)——两主光照车道合同与 IBL 实现；
- [Raylib 引擎能力标准化 Showcase](engine-capability-showcases.md)——三层分层与标准化合同本文的上位文档。
