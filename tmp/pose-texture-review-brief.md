# 复审：姿势纹理蒙皮（bone palette texture + 每实例索引）在 GL 3.3 / raylib 5.5 / 本仓库约束内是否成立，容量重估

## 背景

上一轮结论"无 SSBO → 无法每实例骨骼 → 差 Unity 3-10 倍"被维护者质疑：Unity 主流大规模人群插件（GPUInstancer 一类，10 万级）不用 VAT，用的是**实时骨骼矩阵存 RGBA32F 纹理 + 顶点着色器按实例 ID texelFetch 采样**——顶点纹理采样（VTF）、texelFetch、gl_InstanceID 全部是 **GL 3.3 / GLSL 330 核心特性**，不需要 SSBO。请重新验证。

## 修正后的设计（"姿势纹理蒙皮"）

1. **骨骼调色板纹理**：RGBA32F，宽 = boneCount×4 texel（每骨骼 mat4 = 4 个 RGBA texel），高 = 姿势数（当前桶的姿势）。每帧只 UpdateTextureRec 更新前进过的姿势行（脏矩形，FieldPresenter 已验证该通道）。
2. **实例表纹理**：RGBA32F 小纹理，每实例 1 texel：x=姿势行号，y-z-w=tint 颜色。顶点着色器 `texelFetch(instanceTable, ivec2(gl_InstanceID,0), 0)` 取姿势行 + tint。
3. **着色器改动**（本仓库蒙皮着色器本就是自研 #version 330，`src/Platforms/Desktop/skinning_instanced.vs`）：`uniform mat4 boneMatrices[128]` → 4 次 texelFetch 取骨骼矩阵；桶键去掉 clipIndex/frameIndex/colorKey → **每 (mesh×material) 一次 draw 覆盖全部姿势与颜色**，draw 数从 112×6=672/pass 塌缩到 6/pass。
4. **每实例模型矩阵**：继续走 raylib `instanceTransform` 属性（现有 CPU 指针通道，64B/实例；10 万实例=6.4MB/帧纯 memcpy）。
5. **纹理绑定**：不碰绑定层——姿势/实例表纹理挂 raylib 材质贴图槽位（shadow 贴图已用 emission 槽，先例在 `RaylibShadowSampling.MaterialSlot`），raylib 自动绑单元并设 locs。
6. CPU 动画采样保持 O(姿势)（姿势桶摊销不变），只是姿势多样性不再吃 draw 数。

## 请验证

1. **GLSL 330 合法性**：顶点着色器 texelFetch RGBA32F（sampler2D 无 mipmap、CLAMP）+ gl_InstanceID——330 核心是否无条件支持？raylib 5.5 rlgl 对纹理单元的占用（材质槽 0..N）会不会与自定义采样器冲突，材质槽挂载方案是否成立？
2. **纹理创建/更新路径**：raylib 5.5 用 `Image{data=float*, format=PIXELFORMAT_UNCOMPRESSED_R32G32B32A32}` + `LoadTextureFromImage` 创建浮点纹理、`UpdateTextureRec` 更新子矩形——经 vendored 绑定（不可改）调用是否完整可用（Image struct 是 unsafe 的、data 是 void*）？
3. **容量重估**：该设计下 draw/uniform 瓶颈消失，剩余瓶颈=顶点蒙皮吞吐×2 pass + 每实例矩阵 memcpy。mannequin 8954 顶点实测 4096 实例 13.1ms（含 CPU 桶采样/uniform/draw 开销，估计渲染纯开销 ~11ms）；去掉桶开销、按纯顶点吞吐外推：8954 顶点网格的上限？换 0.5-1.5k 顶点人群网格 + LOD 后的上限？能否到 Unity 插件口径的 5-10 万？
4. **风险清单**：浮点纹理精度（mat4 存 RGBA32F 无量化误差✓？）、纹理尺寸上限（GL 3.3 保证 MAX_TEXTURE_SIZE≥2048/8192，姿势数×4 texel 宽度是否撞顶）、阴影 pass 的深度着色器同步改造、instanceTransform 继续走指针上传的量级。
5. **回答"升级 raylib 版本有没有用"**：raylib 5.5→更新版本是否仍锁 GL 3.3（无 SSBO/compute/indirect）？若是，本设计是否证明**不升级也够**？

输出：设计成立性逐项判定 + 修正后的容量数字 + 与 Unity 10 万级口径的重新对标 + 风险与最短落地清单。用中文。
