# 资产类型 AssetKind 逐条

AssetKind 回答"这个 behavior 绑的是什么类别的可视输出"。作者写在 `assetBinding.assetKind`。总目录见 [README.md](README.md)。

### Mesh — 普通网格

- **是什么**：静态/可移动网格（建筑、道具、单位模型），数量大走 `InstancedStaticMesh` 合批，少量走 `StaticMesh` 单件带光照。
- **怎么写**：`assetKind: "Mesh"` + `renderPath`（选车道见 [render-lanes.md](render-lanes.md)）；自己的模型在 `host_assets.json` 以 `backendId: "raylib"` 绑 GLB 源文件。
- **跑**：preset `engine_raylib_primitives`（程序图元基线）、`raylib_client_parity_raylib`（glTF 模型 + 队伍色）。
- **证据**：`artifacts/acceptance/engine_raylib_primitives/screen.png` + `stats.json`；逐场讲解见 [engine-gallery-wiki/primitives](../engine-gallery-wiki/README.md)。

### SkinnedMesh — 骨骼蒙皮模型

- **是什么**：带骨骼动画的角色模型；同模型大种群走 `GpuSkinnedInstance` 车道（GPU 顶点蒙皮 + `(clip,frame)` 桶化合批），单件预览走 `SkinnedMesh`。
- **怎么写**：`assetKind: "SkinnedMesh"` + `renderPath: "GpuSkinnedInstance"`，配 `Animator` behavior（控制器/档案 id）。
- **跑**：preset `engine_raylib_gpu_skinning`（蒙皮机制）、`engine_raylib_crowd_anim`（4096 动画实例人群）。
- **证据**：`artifacts/acceptance/engine_raylib_crowd_anim/screen.png` + `stats.json`，录像 `artifacts/evidence/engine_raylib_crowd_anim/play.mp4`；蒙皮运行时合同验收 `artifacts/acceptance/presentation-skinned-runtime-contract/battle-report.md`。

### Decal — 投影贴花

- **是什么**：脚印、弹坑、选择标记等贴合接收面的印记（投影盒语义，非平面贴片）。
- **怎么写**：`assetKind: "Decal"`；接收面目前以高度图地形为主，缺投影器的接收面 fail-loud。
- **跑**：preset `engine_raylib_decal_projection`（地形投影）、`raylib_visual_atmosphere_raylib`（场景内摆放置）。
- **证据**：`artifacts/acceptance/engine_raylib_decal_projection/screen.png` + `stats.json`。

### VFX — Quarks 粒子

- **是什么**：`quarks.ludots.v1` 粒子效果：Once/Loop、Billboard/StretchedBillboard/Primitive/Trail 四种渲染模式、flipbook 贴图序列、种子可注入。
- **怎么写**：`particle_vfx.json` 定义效果，`mesh_assets.json` 声明 VFX 句柄，`assetBinding.assetKind: "VFX"` 绑到 behavior。字段全表见 [Quarks 粒子 Schema](../../architecture/quarks-particle-schema.md) 与 [Raylib 渲染配置结构](../raylib-render-config-structure.md)。
- **跑**：preset `engine_raylib_particles`（三种模式基线）、`vfx_forge_raylib`（九种效果锻造台）。
- **证据**：`artifacts/acceptance/engine_raylib_particles/screen.png` + `stats.json`；VFX Forge 的验收登记见 `showcase.registry.json` 的 `vfx_forge` 条目。

### Sound — 声音（契约就绪，执行缺口）

- **是什么**：3D 世界位置声音请求（loop/volume/stableId 生命周期），由 `Sound` behavior 产出 `SoundRequestBuffer`。
- **现状**：Core 契约与行为系统完整，**raylib 适配器尚无音频设备消费**——这是 presentation 域唯一整块缺失的能力面；映射评估与收口清单见治理报告。
- **学习材料**：契约源 `src/Core/Presentation/Requests/SoundRequest.cs`；[Presenter Raylib UAT](../../architecture/presenter-raylib-uat.md) 声音章节的双视角验收表。

### Spline — 样条

- **是什么**：道路/河流可见条带（Render 用途）与巡逻路线驱动（Patrol 用途，带 waypoint 事件与 pingPong）。
- **怎么写**：`assetBinding.assetKind: "Spline"` 或独立 `Spline` behavior（`usage: "Render" | "Patrol"`）。
- **跑**：preset `engine_raylib_ribbon_overlay`（条带渲染）、`presenter_blacksmith_showcase_raylib`（工人样条巡逻全链路）。
- **证据**：`artifacts/acceptance/engine_raylib_ribbon_overlay/screen.png` + `stats.json`。

### WorldHud — 世界空间 HUD

- **是什么**：血条、名字板等钉在世界坐标、随距离缩小的 HUD；引擎投影到屏幕后批量绘制。
- **怎么写**：`assetKind: "WorldHud"`；铁匠铺的 hudbar/hudtext 基准是活样例（含 5 万级 hotpath 验收）。
- **跑**：preset `presenter_blacksmith_showcase_raylib`。
- **证据**：`artifacts/acceptance/presentation-hotpath-harness/battle-report.md`（HUD hotpath 基线）；UAT 双视角表见 [Presenter Raylib UAT](../../architecture/presenter-raylib-uat.md)。

### WorldText — 浮动文字

- **是什么**：一次性浮动战斗文字/提示（可配 `DefaultLifetime` 自动回收），带 yDrift 上浮。
- **怎么写**：`WorldText` behavior（`textToken` 走文本目录本地化，`valueParamKey` 绑黑板数值）。
- **跑**：preset `presenter_blacksmith_showcase_raylib`。
- **证据**：文本合同测试与验收见 [Presenter Raylib UAT](../../architecture/presenter-raylib-uat.md) WorldText 章节。

### GroundOverlay — 地面指示

- **是什么**：贴地的范围圈/扇形/路径指示（Circle/Cone/Line/Ring，填充+边框+可调宽度）。
- **怎么写**：`assetKind: "GroundOverlay"`，几何参数可绑黑板 param（如 `groundOverlay.length` 由规则写入）。
- **跑**：preset `presenter_blacksmith_showcase_raylib`、`raylib_client_parity_raylib`。
- **证据**：UAT 地面指示章节（`artifacts/acceptance/` 下铁匠铺链路 trace）。

### Surface — 地形表面

- **是什么**：chunk 化地形表面车道（与 Mesh 车道正交的另一条专用通道），由 `SurfaceSource` behavior 驱动烘焙与流送。
- **怎么写**：`assetKind: "Surface"` + `SurfaceSource` behavior（geometrySource/chunkBake/materialSet/lodProfileId）。
- **跑**：preset `engine_raylib_terrain_surface`（表面车道）、`engine_raylib_terrain_heightmap`（高度图形态）。
- **证据**：`artifacts/acceptance/engine_raylib_terrain_surface/screen.png` + `stats.json`；chunk 流送验收 `artifacts/acceptance/chunk_streaming_showcase/battle-report.md`。
