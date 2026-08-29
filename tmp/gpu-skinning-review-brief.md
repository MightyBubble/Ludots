# 评审请求：GPU 蒙皮 + 大规模实例动画，能否对标 Unity/Unreal 的大规模合批性能？

## 当前实现（代码事实）

1. **分桶**：`GpuSkinnedInstanceBatchKey(meshAssetId, materialId, colorKey, clipIndex, frameIndex)`——同动画姿势（clip+帧号）的实例进同一桶。crowd_anim 场景 4096 实例、16 个相位桶 × 7 色带 ≈ 最多 112 桶。
2. **每桶一次 CPU 动画采样**：`Rl.UpdateModelAnimationBones(model, anim, frameIndex)` 每桶每帧一次（桶内全部实例摊销，不是每实例）。
3. **骨骼上传**：每桶每 mesh 一次 `rlSetUniformMatrices(_locBoneMatrices, mesh.boneMatrices, boneCount)`——GL uniform 通道，骨骼数上限 128（MaxBones）。
4. **蒙皮在顶点着色器**，随后 `DrawMeshInstanced(mesh, material, transforms+offset, chunkCount)`，单 draw 上限 32768 实例，实例矩阵从 CPU 指针上传（W4a 结论：绑定层无 buffer API，无法持久驻留，见 #1329）。
5. 矩阵 CPU 构建（revision 缓存，静态命中不重建）；逐实例视锥剔除在提交点压缩（#1331）。
6. 约束：raylib 5.5 / OpenGL 3.3 / vendored 绑定不可改、无 compute shader、无 SSBO（GL 3.3）、无 indirect draw。

## 实测（本机，Release，隐藏窗口）

- crowd_anim（4096 蒙皮实例，mannequin 环形行军）：avg 13.1ms/帧（≈76fps），p95 16.0ms——**含窗口轮询/相机/画廊文字 overlay 的整帧**，非纯渲染。
- dynamic worker 基准（30k presenter 实体）：视锥剔除后平均 214 个 GPU 蒙皮实例/帧，零 drop。
- 无蒙皮对照：primitives 场景（纯图元矩阵波动）2.1ms/帧。

## 对照目标（商业引擎公开口径）

- Unity DOTS/Hybrid + GPU instancing 动画演示：1万-5万带骨骼动画角色 @60fps（中端硬件）；技术要点：骨骼矩阵按实例写 structured buffer、compute/顶点着色器蒙皮、实例数据 GPU 常驻、indirect draw。
- Unreal Mass/Niagara 人群：1万-10万 agent，但动画多为烘焙 VAT（顶点动画纹理）而非实时骨骼。

## 请回答

1. **定量判断**：当前架构（姿势共享桶 + uniform 骨骼 + CPU 指针实例矩阵）在同一中端硬件上的理论容量上限大约多少个实时骨骼动画实例 @60fps？与 Unity DOTS 的 1-5万 相比差几倍？瓶颈拆解（CPU 动画采样×桶数、uniform 上传×桶数×mesh、矩阵 CPU 上传、draw 数）各自占多少量级？
2. **架构级差距**：哪些差距是本质的（GL 3.3 无 SSBO/compute/indirect），哪些只是工程量（CPU→GPU 上传路径）？"姿势共享桶"相对 Unity 的"每实例独立骨骼"其实是人群同步动画的更优摊销——这个优势能兑现到什么规模？
3. **诚实结论**：如果目标是"验收管线"（验证 Core 侧万人级模拟的表现层），当前够用到几万？如果目标是对标 Unity/Unreal 的人群演示产品级，差距清单和最短补齐路径（在 raylib/GL3.3 约束内 vs 必须突破约束）？
4. 给一个分档结论：当前处于商业引擎大规模合批能力的什么百分比/什么档位，为什么。

输出：定量分析 + 明确结论。用中文。
