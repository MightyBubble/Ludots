# GPU 骨骼实例化与离线开源重定向

## 1. 概述

两条互补基建：

1. **离线开源重定向**：无头 Blender（GPL）按数据驱动骨名表，把源动画烤到目标骨架，导出 GLB。运行时不再猜手指/插槽差异。
2. **真 GPU 骨骼实例化**：CPU 采样动画得到骨骼矩阵调色板，GPU 顶点着色器做蒙皮；同一 `(clip, frame)` 桶内用 `DrawMeshInstanced` 画大批实例。**禁止 VAT**。

不引入 `raylib-3d-anim-system`（热路径分配、共享 Model 姿势、无实例化蒙皮）。

## 2. 结构

```text
tools/animation_retarget/          # 无头离线重定向（Blender + JSON 映射）
  retarget_bake.py                 # blender --background --python 入口
  run_retarget.sh                  # CI/本地包装
  mappings/*.json                  # 骨名映射 SSOT（data-driven）

src/Platforms/Desktop/
  skinning_instanced.vs/.fs        # 实例变换 + boneMatrices[] 蒙皮

src/Libraries/Raylib-cs/           # Mesh ABI + LoadModelAnimations 绑定
src/Client/.../RaylibPrimitiveRenderer.cs
                                   # GpuSkinnedInstance：palette 桶 + 实例化绘制
```

## 3. 详情

### 3.1 离线重定向合同

- 输入：`source.glb`（动画）、`target.glb`（目标网格+皮肤）、`mapping.json`
- 映射：`sourceBone -> targetBone`；未映射目标骨保持绑定姿势（忽略多余手指/插槽）
- 流程：导入 → 按名建约束/拷贝局部变换 → Bake → 删源骨架 → 导出 GLB
- 失败：缺映射文件、目标无皮肤、Bake 后动画数为 0 → **非零退出，禁止静默**

### 3.2 GPU 骨骼实例化合同

- 采样：`LoadModelAnimations` + 帧索引 → `UpdateModelAnimationBones`（写 `mesh.boneMatrices`）
- 分桶键：`(meshAssetId, materialId, clipIndex, frameIndex, colorKey)`
- 绘制：上传该桶 `boneMatrices[MAX_BONES]`，再 `DrawMeshInstanced(worldMatrices)`
- 着色器：`skin(boneIds, boneWeights, boneMatrices) * instanceTransform * mvp`
- 不同步动画时间的实例进不同桶；同姿势共享一次骨骼上传

### 3.3 复用与禁区

| 复用 | 不做 |
|------|------|
| `GpuSkinnedInstance` 车道 | VAT / 顶点动画贴图 |
| 现有 ISM `DrawMeshInstanced` | 接入 raylib-3d-anim-system |
| `host_assets` / mesh 登记 | 运行时静默降级为静态网格 |
| AnimatorPackedState → clip/frame | 热路径结构变更 |

## 4. 场景

- 作者把 KayKit Medium 动画烤到自有角色 GLB，登记进 Mod，表演者走 `GpuSkinnedInstance`。
- 战场上数千同款士兵播 `Walking_A` 的相近帧：同桶一次骨骼上传 + 实例化绘制。
- 小号人偶缺 `handslot`：离线映射忽略或补骨后导出，运行时 `IsModelAnimationValid` 为真。

## 5. 边界

- 离线工具只保证骨名映射 + 绑定姿势填充；极端体型差需人工调映射，不在运行时“智能修复”。
- 单桶骨骼数上限由着色器 `MAX_BONE_NUM`（默认 128）约束，超出 fail-loud。
- 每实例完全独立时间线会使桶数上升；仍是 GPU 蒙皮，不是 VAT。
- OBJ/FBX/DAE 在运行时经 `RaylibModelFileConverter`（Assimp）转 GLB 后装载，转换结果按源文件哈希磁盘缓存；纯 GLTF/GLB 直接原生装载。

## 6. UAT

```gherkin
Feature: 离线开源重定向后大批量 GPU 骨骼实例能看见走路
  作为玩法作者
  我想把外部动画烤到统一骨架并在战场上画出大批走动单位
  以便不依赖专有工具、也不用顶点动画贴图假动作

  Scenario: 无头重定向产出可校验的同骨架动画
    Given 仓库内有目标角色 GLB 与源动画 GLB
    And 存在数据驱动的骨名映射 JSON
    When 我在无图形环境下执行 tools/animation_retarget/run_retarget.sh
    Then 命令以退出码 0 结束
    And 输出 GLB 能被 Raylib LoadModel 与 LoadModelAnimations 加载
    And IsModelAnimationValid 对所选 clip 返回 true

  Scenario: 玩家看到大批单位用真骨骼蒙皮走路
    Given 已烤好的同骨架角色动画已登记到展示资产
    And 场景生成不少于 1000 个 GpuSkinnedInstance 单位并播放行走 clip
    When 玩家进入该场景并观察前几秒
    Then 单位网格随骨骼弯曲摆动而不是整块平移的假动作
    And 诊断计数显示 gpuSkinned 实例数与批次数大于 0
    And 未启用任何 VAT 资源路径
```
