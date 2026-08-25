# 渲染车道 VisualRenderPath 逐条

车道回答"这批可视物用哪条 GPU 管线画"。车道选错不是性能问题，是**合同错误**（装载/执行期 fail-loud，静态车道禁带动画载荷）。总目录见 [README.md](README.md)。

| 车道 | 适用 | 演示 preset |
|---|---|---|
| `StaticMesh` | 少量单件带光照模型 | `engine_raylib_primitives` / `engine_raylib_lighting` |
| `InstancedStaticMesh` | 大量静态网格合批（植被/建筑群） | `engine_raylib_instancing` / `capability_standard_static_presenter_30k_raylib` |
| `HierarchicalInstancedStaticMesh` | 层级实例化合批（外部批量源） | `engine_raylib_instancing` |
| `SkinnedMesh` | 单件骨骼模型预览 | `engine_raylib_gpu_skinning` |
| `GpuSkinnedInstance` | 同模型动画种群 GPU 蒙皮合批 | `engine_raylib_crowd_anim` |
| `Surface` | chunk 地形表面专用通道 | `engine_raylib_terrain_surface` |

各条目证据统一为 `artifacts/acceptance/<preset>/screen.png` + `stats.json`（120 帧统计合同）；逐场讲解与录像见 [引擎画廊 Wiki](../engine-gallery-wiki/README.md)。

### StaticMesh — 单件带光照

Cook-Torrance GGX + split-sum IBL + 方向光阴影；材质面全走 `material_assets.json`（实例链/标量 PBR/命名参数直推 uniform）。

### InstancedStaticMesh — 静态合批

实例化绘制一条 draw 撑数万实例；`engine_raylib_instancing` 场景可从 3k 拉到 300k 实例（帧统计在 stats.json）。外部批量源形态（InstancedBatch behavior）走 HISM 车道，源合同见 [Instanced Batch 外部 Source Contract](../../architecture/instanced-batch-source-contract.md)。

### SkinnedMesh / GpuSkinnedInstance — 蒙皮两形态

单件预览与种群合批；`(clip,frame)` 桶化 + 顶点着色器蒙皮，运行时合同验收 `artifacts/acceptance/presentation-skinned-runtime-contract/battle-report.md`。

### Surface — 地形表面

与 Mesh 车道正交的 chunk 通道；烘焙/流送/LOD 由 `SurfaceSource` behavior 声明（见 [behaviors.md](behaviors.md) SurfaceSource 条目）。

### LOD 与裁剪（横切能力）

- **LOD 档案**：`VisualLodProfile`（`assets/Presentation/lod_profiles.json`）按距离分档切 mesh/裁剪。
- **相机裁剪**：owner entity 的 `CullState` 继承到 presenter 树，被裁剪即跳过 emit；剔除策略由全局 `presentation.cameraCulling` 配置（不在 presenter 档案里逐个声明）。
- **HUD 投影**：世界 HUD 经投影快照批量转屏幕坐标，hotpath 基线见 `artifacts/acceptance/presentation-hotpath-harness/battle-report.md`。
