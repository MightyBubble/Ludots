# 地形 LOD/裁剪对齐 + 迷雾三态渲染底座 —— 调查报告

分支 `feat/crowdsim-navsurface`（PR #1732）。调查日期 2026-10-09。记账来源：2026-10-07 调查（PR #1732 评论 6034365701）。

## 一、地形 LOD/裁剪逐项复核

复核方式：通读 `src/Client/Ludots.Raylib.Render/Rendering/RaylibContinuousHeightmapRenderer.cs`（2024 行）全文与全仓消费者 grep，对每条记账给出现状判定；随后在自建验收夹具（`mods/fixtures/terrain/TerrainLodCullingAcceptanceMod`，程序化四段运镜：低空巡飞 → 斜视旋转 → 拉远驻留 overview → 快速回切）上跑 BEFORE/AFTER 取证。

### B3 跨 chunk 法线单边差分接缝 —— 证实未修，已修

现状（修复前）：`ComputeNormal` 把邻居采样钳位在 chunk 自身样本内（`Math.Max(0, x-1)` / `Math.Min(SampleColumns-1, x+1)`）。chunk 边界顶点退化为单边差分，与邻 chunk 在共享列上的单边差分不一致，形成逐 chunk 的着色接缝。`ContinuousHeightmapRenderChunk.TryReadHeightCm` 本身只能读块内样本（`ContinuousHeightmapRenderChunk.cs:94-118`）。

处置：渲染源同时实现 `IContinuousHeightmap` 时（本引擎全部现状源都满足），边界邻居高度改经 `TrySampleHeightCm` 跨界取真实邻格（双线性在格点上取原值），并把差分跨度从钳位后的 1 步恢复为真实 2 步；地图最外缘采样失败回退单边差分。内部顶点路径不变。渲染源不可采样时保持旧行为。

证据：
- 单测 `ComputeNormal_BorderSampleWithWorldSampler_MatchesUnifiedFieldNormal`：非线性高程下，带跨界采样的边界法线与统一场法线逐分量相等；两侧 chunk 在共享列上的法线一致（接缝消除合同）；无采样器回退路径与统一场不一致（接缝来源证明）。
- 前后对照截图 `artifacts/acceptance/terrain-lod-culling/{before,after}/shot_001_f0200.png`：同帧像素差分 3.89% 像素 >30（接缝着色修正带）、meanAbsDiff 14.59、无成片空洞（若有误剔可见地形会出现背景色大片差异）。差分图 `diff_shot_001_f0200.png`。

### B4 ChunkLodErrorPx 死配置 —— 证实，按"删键"处置

现状：`ChunkLodErrorPx` 仅存在于 `tools/crowdsimulation-export/exportLudots.mjs`（写入）与 5 个地图 JSON（mass_navigation、crowd_simulation_s1337/s2024/s7、east_asia_visual_heightmap）。全仓 .cs 无任何读取；`ContinuousHeightmapRenderProfile` 没有该属性，反序列化静默丢弃。运行时实际 LOD 形态是两档：均匀分辨率 chunk 窗口 + footprint 驱动的 overview 网格，活配置是 `OverviewSwitchChunkSpans` 与 `OverviewVertexLimit`（`RaylibContinuousHeightmapRenderer.cs:455,470` 消费）。

处置：导出器与 5 个地图删除该键，配置不再撒谎。不实现逐 chunk 屏幕误差 LOD 的理由：那是一条新管线（距离分档采样 + 相邻档位 T-junction 裂缝缝合/裙边），按《AI 辅助开发规范》判断三"缺一整条管线先停下说明缺口，不在 feature 中临时造"。缺口已在此上报；如需对齐 Unreal/Unity 的逐块 LOD，应立独立工单。

证据：`grep -rn ChunkLodErrorPx --include="*.json" --include="*.mjs" mods/ tools/` 归零。

### D1 驱逐策略抖动（overview 驻留全量驱逐 + 回切爆发重建）—— 证实未修，已修

现状（修复前）：`EvictUnusedChunks(240)` 在 overview 分支每帧执行；chunk 帧龄停更后 240 帧全量逐出。实测（阴影关闭，`before/diag-noshadow-f1.txt`）：回切帧 1037 terrain=225.26ms（正常 8ms 的 28 倍）、built=1936（全窗一次性重建）。

处置过程中的中间版本教训：只在 overview 期间"跳过驱逐"不够——回切后第一个 chunk 帧的帧龄驱逐照样全量逐出（实测仍连续两帧 215/205ms）。最终修复：overview 驻留期间把缓存 chunk 钉住（刷新 `LastUsedFrame`），回切零驱逐零重建；chunk 模式平移时的 240 帧窗口驱逐照常（摊销语义不变）。代价：overview 驻留期间缓存常驻内存，上限仍由 chunk 模式窗口驱逐约束。

证据：`after/diag-noshadow-f1b.txt`：回切窗口 f1000-1100 最大 terrain=1.56ms、built=0。BEFORE 同窗口 225ms/1936。
附带发现：阴影开启时 `RenderShadow` 每帧经 `GetOrCreateChunk` 刷全窗帧龄，D1 缺陷被掩盖——D1 取证必须用 `LUDOTS_RAYLIB_DRAW_SHADOWS=false`。

### D2 无视锥剔除（窗口与朝向无关）—— 证实未修，已修

现状（修复前）：chunk 窗口是以 camera.target 为中心的方形（半边 `max(VisibleRadiusCm=140000, footprint)`），与朝向/视锥无关；窗口内全部绘制且逐块构建。

处置：`Render()` 主通道逐 chunk 对当帧视锥做 AABB 剔除（行向量 Gribb–Hartmann 六平面，`ExtractFrustumPlanes` + `IsAabbOutsideFrustum`）；已建块用紧 Y 界，未建块用垂直棱柱保守判定（顶/底平面永不拒绝，等效 XZ 投影剔除）。剔除发生在 `GetOrCreateChunk` 之前，屏幕外块零构建零绘制。`RenderShadow` 不剔除（主视野外的投射者仍需入影）。中途教训：第一版把行向量约定当成列向量取平面（基向量按行放置），单测用简单相机暴露不出来，补了"应用轨道相机约定"复现测试后修正为按列。

证据（同机同帧，`before/diag.txt` vs `after/diag.txt`，terrainChunks=drawn/built）：

| 帧 | 场景 | BEFORE drawn / ms | AFTER drawn / ms |
|---|---|---|---|
| 200 | 低空巡飞 | 2025 / 8.22 | 6 / 0.44 |
| 380 | 斜视旋转 | 1936 / 7.90 | 94 / 1.16 |
| 460 | 斜视旋转 | 1936 / 8.08 | 89 / 0.64 |
| 1040/1100 | 回切近景 | 1936 / ~8 | 7 / ~0.7 |

截图前后内容一致（差分仅接缝带），证明剔除没有掉可见块。

## 二、迷雾三态渲染底座

### 现有接线到哪一步（复核结论）

数据链路在调查时已全通：`VisionSystem`（ECS，`VisionEmitterCm` 实体每帧写入）→ `FogFieldStore`/`FogField`（`ChunkedField2D<CellVisibility>`，Unseen 默认、脏块追踪、Visible 老化为 Explored）→ `FogGlobalFieldVisualProjector`（宿主 post-tick，`RaylibHostLoop.cs:739`）→ `GlobalFieldVisualBuffer` → `RaylibFieldRenderPresenter`。渲染端缺口：Fog 场画成**固定 Y=0.08m 的水平平面四边形**（山体穿破）、调试配色（可见=淡蓝）、POINT 过滤（硬边）、不随地形。

### 处置（复用贴花通道，未新造渲染路）

- `terrain.fs`：叠加混合强度从硬编码 `* 0.35` 改为 `uniform float uNavWalkabilityBlend`（流场/可走层默认仍 0.35，行为不变；迷雾 1.0 表达"不可见=黑"）。
- `RaylibContinuousHeightmapRenderer.SetNavWalkabilityOverlayExternal` 增加可选 `blendStrength`（默认不变），清绑复位默认值。
- `RaylibFieldRenderPresenter`：迷雾配色改为市面主流三态——不可见=黑(a=232)、已探索不可见=暗化黑(a=148)、可见=清晰(a=0)；Denied 保留暗红遮蔽提示（数据模型多出的第四态，不丢信息）。Fog 纹理改双线性过滤（软边缘）。新增 `DrawFogKind` 开关。
- `RaylibHostLoop`：贴花槽位选择器从"Flow/Walkable 优先"扩展为"Flow/Walkable 优先，其次 Fog"（槽位唯一，流场在场时迷雾回退平面路径——现状限制，见下）。

现状限制（如实记录）：贴花槽位（MATERIAL_MAP_HEIGHT）唯一，流场与迷雾不能同时占用；overview 网格下贴花经 `NavWalkabilityOverlayVisibleInOverview` 保持全缩放可见。

### 演示场景（`mods/showcases/fog_of_war/FogTerrainDecalShowcaseMod`）

4km 起伏地形（`scripts/fog-of-war/gen_fog_terrain_decal_height.py` 生成）+ presentation 侧演示写入器：一个视野源沿 Lissajous 路径移动，每帧 `AgeVisibleToExplored` + 填当前视野盘（240m 半径）为 Visible；首帧在对角各放一个已探索格把场包围盒钉到全图（迷雾贴花覆盖域=场非默认格包围盒，否则轨迹外区域完全清晰）。写入的是渲染底座消费的真实数据结构（`FogFieldStore`/`FogField`），内核视野组 F02 落地后由真实视野数据接管同一通道，演示写入器即可移除——本单未替 F02 设计任何内核 API。

证据：`artifacts/acceptance/fog-terrain-decal/shot_00{1..4}_f0{300,720,1140,380}.png` 四帧序列：视野盘（清晰地形）随源移动、身后暗化轨迹亮度 35-108 随地形起伏变化（平面遮罩会是均匀暗带）、全图其余=黑。诊断计数 fieldCount=1/fieldDirty=1/fieldArea=14641（盘 bbox 每帧增量上传）。

## 三、验收配方与复现

```bash
# 地形 BEFORE/AFTER（夹具 mod: TerrainLodCullingAcceptanceMod,相机脚本四段运镜）
./scripts/run-mod-launcher.cmd cli resolve mod:TerrainLodCullingAcceptanceMod --adapter raylib   # 生成计划图
# dotnet build 后 launcher.runtime.json 会被重置,需按 graph 补全计划元数据(见下"基建缺口")
cd src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net9.0
LUDOTS_AUTO_EXIT_FRAME=1250 LUDOTS_TAKE_SCREENSHOT_PATH=.../shot.png LUDOTS_TAKE_SCREENSHOT_FRAMES=200,380,460,1040,1100 \
LUDOTS_MIN_RUNTIME_MS_BEFORE_SCREENSHOT=3000 LUDOTS_RAYLIB_DIAGNOSTIC_PATH=.../diag.txt \
LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES=10 dotnet Ludots.App.Raylib.dll
# D1 取证加: LUDOTS_RAYLIB_DRAW_SHADOWS=false + TIMING_LOG_INTERVAL_FRAMES=1
# 迷雾演示: cli resolve mod:FogTerrainDecalShowcaseMod 后同上
```

地形资产再生成：`python scripts/terrain-lod/gen_terrain_lod_culling_height.py`、`python scripts/fog-of-war/gen_fog_terrain_decal_height.py`。

## 四、测试与构建

- RaylibAdapterTests 322/322 绿（含新增：视锥 3 测、跨界法线 1 测、迷雾三态色 1 测、Fog 开关 1 测、着色器契约补 uNavWalkabilityBlend 断言）。
- PresentationTests 中唯一触及本单渲染器的 ProjectedDecalContractTests 27/27 绿。
- GasTests(Vision|FogOfWar 过滤) 22 例中 1 失败：`ClientApplier_CreatesFormalSouthernMirrorAndAuthorsSouthernVisionScope`（RTS Frontline schema 701 拒载）。已在本单改动之前的分支状态（干净检出 HEAD~1）复现同样失败——**先在失败，非本单回归**；该域属 crowd 内核整改范围，本单不越界处理。
- 全量 PresentationTests 首轮（与截图取证 GL 并发同跑）26 失败/1170 通过；独立重跑仍 16 失败，集中在 mass-navigation/route/avoidance/showcase 验收域。抽查 3 个代表失败（MarqueeHitAccountsForTerrainHeight、RouteSink_AppliesWaypointOnly…、OverlaySceneBuilder_MapsHudAndOverlayBuffers…）在本单改动之前的干净基线 HEAD~1 上 3/3 复现——**先在失败，非本单回归**。

## 五、基建缺口与观察（上报）

1. **无逐 chunk 屏幕误差 LOD 管线**（B4 的根因）：运行时两档 LOD 是当前架构事实；`ChunkLodErrorPx` 语义的真实现需要新管线（分档采样 + 裂缝缝合），应立独立工单评估。
2. **`dotnet build` 会把 `launcher.runtime.json` 重置为极简指针版**，而 graph 的 `runtimeArtifacts.bootstrapArtifactPath` 又强校验运行时配置文件名与全量计划元数据——`cli resolve` 生成的极简版无法直接 dotnet-direct 启动，`cli launch` 才写全量版。取证要 dotnet-direct 时需手工补全（本单验收脚本内有现成形状）。建议 launcher 后端补一个"resolve 即写全量 bootstrap"或 build 后不复位。
3. **共享机器上取证运行会被外部关窗/截图校验器连带击杀**：`ValidateRuntimeScreenshotEvidence` 发现画面平坦会抛异常终止整个序列（这是对的），但一次 1250 帧取证被外部打断就得整轮重跑。实验过 `FLAG_WINDOW_HIDDEN`——隐藏窗口下交换链不呈现、截图全黑，不能用于取证。
4. **InputCollection（sim 侧）系统在部分运行中停止被调用**（根因未定位；presentation 侧系统全程稳定）。本单的运镜脚本因此走 presentation 注册。此现象值得在仿真管线下单排查时一并留意。

## 六、提交拆分

1. `fix(raylib): 地形 LOD/裁剪对齐` —— B3/D1/D2 修复 + B4 删键 + 验收夹具 mod + 地形生成脚本 + 证据目录。
2. `feat(raylib): 迷雾三态渲染底座` —— 着色器/渲染器/演示 mod + 演示生成脚本 + 证据目录。
