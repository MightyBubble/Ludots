Part of #1387 · 姿势纹理蒙皮——渲染器侧落地与着色器修复

## 双评审已确认的缺陷（必须修）

### 着色器（skinning_instanced_pose_texture.vs / shadow_depth_skinning_pose_texture.vs）

1. **双重染色**：VS 里 `fragColor = vertexColor.rgb * tint` 后，现有 FS 又乘 uniform `tint`。修：VS 只传 tint 作为 varying，FS 里不再乘 uniform tint（或反过来）。
2. **实例表丢 alpha**：实例表只存 RGB 三通道 tint，丢了 `Color.W`（alpha）。修：实例表 1 texel 存 poseRow(float) + RGBA tint 需要 2 texel 或改用 2 texel/实例。
3. **uBonePaletteWidth 未使用**：声明但被链接器优化掉，渲染器不该强制取它的 location。修：删掉该 uniform，宽度和 boneCount 由渲染器管理。

### CPU 侧（RaylibGpuSkinnedBatchRenderer）

4. **矩阵不能顺序拷贝**：`RaylibMatrix` 字段声明为行序 `m0,m4,m8,m12 / m1,m5...`，但 GLSL `mat4(c0,c1,c2,c3)` 按列读。写入调色板时必须按 `(m0,m1,m2,m3), (m4,m5,m6,m7)...` 列序重排。
5. **Mesh.boneMatrices 是 native 指针**：`UpdateModelAnimationBones` 调用后必须立刻复制到托管 staging 数组，不能保存 native 指针等后续上传。

### 需实测确认（不盲改）

6. **attribute location 6/7 vs 7/8**：pi 指出 raylib 5.5 默认 VAO 布局 boneIds=6/boneWeights=7，但现有着色器用 7/8 且渲染正确（渲染器走 `GetShaderLocationAttrib` 动态查询）。不盲改——先在画廊跑 A/B 截图对比确认。

## 落地方案（codex 路径 + pi 补充）

- **桶键**：`(meshAssetId, materialId, colorKey, clipIndex, frameIndex)` → `(meshAssetId, materialId)`；batch 内每实例存 transform + poseRow + tint
- **姿势缓存**：键 `(meshAssetId, clipIndex, frameIndex)`，首次调 `UpdateModelAnimationBones` 后立刻把每个 mesh 的 `boneMatrices` native 指针**按列序重排**复制到调色板行
- **调色板纹理**：RGBA32F，width = 128×4 = 512 texel，height = 姿势容量；脏行 `UpdateTextureRec` 更新；POINT/CLAMP/无mipmap
- **实例表纹理**：RGBA32F，固定宽 1024，高度按需；每实例 1-2 texel 存 poseRow + RGBA tint；`uInstanceBase` uniform 支持 chunk 偏移
- **材质槽**：slot 4 (OCCLUSION) = 调色板，slot 6 (HEIGHT) = 实例表（避开 slot 5 阴影 / slot 7 IBL cubemap / slot 10 IBL BRDF）
- **绑定层**：补 `rlLoadTexture` DllImport（绑定缺此入口，按 `rlLoadTextureCubemap` cubemap 先例在 `RaylibNativeResources` 门面加）
- **阴影响着色器同步改造**：共用调色板/实例表，只算位置；删除 `boneMatrices[128]` uniform 残留及 `_locBoneMatrices < 0` 的 throw

## 验收标准

- crowd_anim 4096 蒙皮实例截图与现状视觉等价
- draw/pass 从 672 降至 ≤12（诊断计数）
- 多姿势多颜色同 draw 正确（纹理索引正确性）
- 阴影位置与主 pass 一致（列序/调色板共用验证）
- 16384+ 实例跨 chunk 姿势正确（2D 寻址 + uInstanceBase）
- mannequin 4096 帧时间对比入报告；1000 顶点网格 16384/32768 档位基准
