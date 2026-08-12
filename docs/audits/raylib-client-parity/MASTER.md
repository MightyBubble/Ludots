# Raylib 客户端商业引擎基线对齐 — 更新总单

> SSOT 登记：本目录。Agent 只改分配给自己的路径；状态写入 `STATUS.md`。  
> 分支：`cursor/gpu-skinned-instancing-ef14`  
> 范围：**只做 Raylib 客户端能力**；Mod 表与 Host 表继续分离；不重做 Core 状态机。

## 1. 概述

目标：作者在 Mod 表挂逻辑资产、在 Host 表（`backendId=raylib`）挂 URI 后，Raylib 客户端能：

1. 加载并显示静态 GPU 实例网格  
2. 加载并播放 GPU 骨骼动画实例（真蒙皮，非 VAT、非绑姿假动画）  
3. 对实例做增删改（位姿/可见/换 clip）  
4. 按 Host 材质表绑定贴图/参数（至少 albedo）  
5. 特效有可登记的 shader/粒子基线（非占位球）

验收以**截图**为准（见第 6 节）。

## 2. 结构（工作包）

| ID | 工作包 | 负责人模式 | 主要路径（独占） |
|----|--------|------------|------------------|
| W1 | GPU 蒙皮生产接线 + 客户端播放面 | subagent | `src/Client/Ludots.Client.Raylib/Rendering/Raylib*GpuSkin*` `RaylibPrimitiveRenderer.cs`（skinned 段）`src/Platforms/Desktop/skinning_instanced.*` |
| W2 | Host 材质绑定 | subagent | 新建 `RaylibMaterialHostBinder.cs`；`RaylibPrimitiveRenderer` 材质解析点；必要时读 host material 描述 |
| W3 | 特效 shader 基线 | subagent | 新建 `RaylibEffectShaderRegistry.cs` + `src/Platforms/Desktop/vfx_*.vs/fs`；VFX 绘制从占位改为可挂 shader（最小可见） |
| W4 | 验收 Showcase + 截图 | subagent（依赖 W1/W2 合并后） | `mods/showcases/raylib_client_parity/` `showcase.registry.json` `artifacts/raylib-client-parity/acceptance/` |

并行约束：

- W1 / W2 / W3 **可并行**（文件独占；W2/W3 不改 skinned 批绘热路径核心逻辑）  
- W4 **等 W1+W2 代码进分支后再跑**截图（可用 W1 探针资产先垫）

## 3. 详情（更新总单）

### W1 — GPU 蒙皮 + 播放面（P0）

- [ ] 生产加载 `skinning_instanced.vs/fs`，GpuSkinned 车道禁止再用静态 `instancing` 冒充  
- [ ] Model 缓存旁路：`LoadModelAnimations`；缺动画 **fail-loud**（GpuSkinned 车道）  
- [ ] 客户端播放面（不依赖重做 Core）：  
  - `RaylibSkinnedPlayback`：`Play(clipIndex|name)` / `SeekNormalized(t)` / `Stop`  
  - 从 `AnimatorPackedState` 读 primary state + normalized time → clip/frame（映射表 data-driven，可先 identity：stateIndex=clipIndex）  
- [ ] 分桶键：`(mesh, material, clip, frame, color)`；每桶一次 `UpdateModelAnimationBones` + `rlSetUniformMatrices` + `DrawMeshInstanced`  
- [ ] 实例增删改：复用现有 skinned batch / snapshot 生命周期；换 clip = 换桶  
- [ ] 禁止静默降级为静态 ISM  

复用：`GpuSkinnedInstance` 车道、`tools/gpu_skinned_instance_probe` 已验证路径、离线 `tools/animation_retarget`。

### W2 — Host 材质绑定（P0）

- [ ] Host `assetKind=Material` + `sourceUris` → Raylib 加载纹理并应用到对应 materialId  
- [ ] 模型绘制：若 host 覆盖 albedo，则覆盖导入材质槽（fail-loud：URI 无法解析）  
- [ ] Tint 仍来自 performer color（已有）  
- [ ] 不宣称完整 PBR；合同写清「albedo 绑定基线」  

复用：`PresentationMaterialRegistry`、`PresentationHostAssetConfigLoader`、Mod/Host 分离。

### W3 — 特效 shader 基线（P1，本长任务内要有最小可见）

- [x] Host 或 Mod 可登记 effect shader 键 → 加载 vs/fs  
- [x] VFX 视觉从纯占位几何改为「billboard/mesh + 可挂 shader」至少一种可见效果  
- [x] 失败 fail-loud，禁止静默白模装特效  

### W4 — 验收产物（P0）

- [x] Showcase Mod：`raylib_client_parity`（静态实例 + GPU 蒙皮人群 + 材质 tint/albedo）  
- [x] `showcase.registry.json` 登记  
- [x] 截图目录：`artifacts/raylib-client-parity/acceptance/`（及 `/opt/cursor/artifacts/...`）  
  - `01_static_ism.png`  
  - `02_gpu_skinned_walk_a/b.png`（两帧差分证明在动）  
  - `03_material_bind.png`  
  - `04_vfx_shader.png`  
- [x] `ACCEPTANCE.md` 合同说明各截图证明点

## 4. 场景（作者视角）

1. 在 Mod `mesh_assets` 登记 `hero.mesh`，在 Host `host_assets` 给 raylib 挂 `hero.glb`  
2. 离线重定向动画烤进同骨架 GLB（或动画同文件）  
3. Performer `renderPath=GpuSkinnedInstance`，Animator 走 Walk  
4. 启动 Raylib showcase → 看见一排小人走路，不是 T-pose 滑步  
5. 改 Host 材质 albedo URI → 重启后衣服贴图变了  

## 5. 边界

- 不做 UE5/Unity adapter；不做 VAT  
- 不引入 `raylib-3d-anim-system`  
- 不在本任务删除 Prefab 全库（标债务即可）  
- InstancedBatch 外部 Source 车道：本总单 **P2**，不阻塞截图验收  
- Core Animator 图不重写；客户端只消费 packed state + 本地 clip 映射  

## 6. UAT（截图验收）

```gherkin
Feature: Raylib 客户端配表即可显示与播动画
  作为内容作者
  我想分开填写 Mod 逻辑表和 Raylib Host 路径表后直接看到结果
  以便不改引擎代码也能验收静态实例、骨骼动画和材质

  Scenario: 静态 GPU 实例可见
    Given Mod 与 Host 已为建筑网格配好 raylib URI
    When 我打开 raylib_client_parity showcase
    Then 截图 01_static_ism 中能看到多栋实例网格

  Scenario: GPU 骨骼动画在走
    Given Host 已挂同骨架角色动画 GLB
    And performer 使用 GpuSkinnedInstance 并处于行走状态
    When 我连续抓取两帧截图
    Then 02_gpu_skinned_walk 两帧角色姿态明显不同
    And 诊断显示 gpu skinned 实例数大于 0

  Scenario: Host 材质绑定可见
    Given Host 为某 materialId 配置了 albedo 贴图 URI
    When 我打开材质验收镜头
    Then 03_material_bind 中目标实例贴图与 URI 资源一致

  Scenario: 特效 shader 最小可见
    Given 已登记一条 effect shader
    When 我打开特效验收镜头
    Then 04_vfx_shader 中能看到非占位球的着色结果
```

## 7. 完成定义（DoD）

- [x] W1–W3 代码合并进本分支并推送  
- [x] W4 四张（或至少 01–03）截图落入 acceptance 目录  
- [x] `STATUS.md` 全部勾选  
- [ ] PR 描述链接本 MASTER 与截图  


> Tracked SSOT: `docs/audits/raylib-client-parity/`. Acceptance screenshots land in `artifacts/raylib-client-parity/acceptance/` and `/opt/cursor/artifacts/raylib-client-parity/acceptance/`.
