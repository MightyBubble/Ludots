Part of #1321 后续 · 由 GPU 蒙皮对标评审（codex+pi 双验证）确立

## 背景

对标评审确认：当前姿势共享桶方案在 4096 蒙皮实例 @60fps 附近触及 draw/uniform 瓶颈（112 桶 × 6 mesh = 672 draw/pass + 672 次骨骼 uniform 上传）；Unity 主流大规模人群插件（GPUInstancer 一类）不用 VAT、不用 SSBO，用**实时骨骼矩阵存 RGBA32F 纹理 + 顶点着色器 texelFetch 按实例采样**——该技术在 GL 3.3 / GLSL 330 / raylib 5.5 / 本仓库 vendored 绑定约束内完全成立，**不须升级 raylib、不须碰绑定层**。

顺带修复一个潜伏缺陷：现有 `uniform mat4 boneMatrices[128]` = 512 vec4，超过 GL 3.3 保证的最低 uniform 向量数（256），在最低规格驱动上可能直接编译失败。

## 设计（双评审验证通过）

1. **姿势调色板纹理**：RGBA32F，宽 = boneCount×4 texel（每骨骼 mat4 = 4 texel，**列主序**），高 = 姿势数。每帧 UpdateTextureRec 只更新前进过的姿势行（脏矩形，FieldPresenter 已验证该通道）。POINT 采样 + CLAMP + 无 mipmap。
2. **实例表纹理（必须 2D）**：RGBA32F，每实例 1 texel：x=姿势行号、y-z-w=tint。2D 寻址 `ivec2(inst % W, inst / W)`（W 取 1024——单行会在 >2048 实例时撞 GL 3.3 最低 MAX_TEXTURE_SIZE 保证，pi 复核抓出的必须项）。每 draw 配 `uInstanceBase` 偏移。
3. **主着色器**（`skinning_instanced.vs`，本就自研 #version 330）：`uniform mat4 boneMatrices[128]` → 4 骨骼各 4 次 texelFetch 构 mat4；`gl_InstanceID` 查实例表取姿势行+tint；权重为 0 的骨骼跳过 fetch 省带宽。
4. **阴影深度着色器同步改造**（`shadow_depth_skinning_instanced.vs`）：共用同一调色板+实例表，只算位置（约 0.5-0.6× 主 pass 成本）。
5. **桶键简化**：`(meshAssetId, materialId, colorKey, clipIndex, frameIndex)` → `(meshAssetId, materialId)`——draw/pass 从 672 塌缩到 ~6；CPU 动画采样保持 O(姿势)（姿势分桶摊销不变，姿势多样性不再吃 draw 数）。
6. **纹理绑定**：姿势/实例表挂 raylib 材质槽（shadow 贴图走 emission 槽的先例在 `RaylibShadowSampling.MaterialSlot`），主/影 pass 统一槽号约定，防 MaterialLibrary 覆盖。

## 容量预期（双评审按本机实测 3.34M 蒙皮顶点/ms 外推，60fps）

| 网格 | 实例上限 |
|---|---|
| mannequin 8954 顶点（现状） | ~4-5k（顶点受限，改善有限） |
| 1500 顶点人群模 | ~1.5-2.4 万 |
| 1000 顶点 | ~2.3-3.7 万 |
| 500 顶点 + LOD | ~4.6-7.3 万 |

10 万级需屏幕有效顶点压到 300-400/实例或更强 GPU——如实标注"数万级"，不对外宣称 10 万。

## 验收标准

- Given crowd_anim 4096 蒙皮实例；When 姿势纹理蒙皮；Then 截图与现状视觉等价（同姿势相位下）；draw/pass 从 672 降到 ≤12（诊断计数）。
- Given 多姿势多颜色实例；When 同一 draw 内；Then 每实例姿势与 tint 正确（纹理索引正确性）。
- Given 阴影开启；When 主+影双 pass；Then 阴影位置与主 pass 一致（列主序/调色板共用验证）。
- Given 16384+ 实例；When 实例表 2D 寻址 + uInstanceBase；Then 跨 chunk 姿势正确。
- 基准：mannequin 4096 实例帧时间对比入报告；新增 1000 顶点网格的大规模基准（16384/32768/65536 档位）。

## 风险（双评审列出）

- GLSL 列主序填矩阵（写错全屏转置——一次性事故点）
- 浮点纹理禁 mipmap；RGBA32F 全精度（不降半浮点）
- 顶点纹理单元占用（GL 3.3 保证 ≥16，本方案用 2 个，安全）
- 每 draw 顶点总量仍是吞吐上限（LOD 按屏幕有效顶点控）
