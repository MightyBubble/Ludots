# GPU 驱动蒙皮管线（SSBO + compute + 直接 GL 绘制）battle report

## 交付
- **compute 姿势求值**：关键帧全局 TRS + bindPose⁻¹ 驻模型数据 SSBO；每帧 compute 合成骨骼矩阵（raylib 5.5 TRS 分解式逐位等价，5330 万元素对账）
- **SSBO 数据层**：姿势 SSBO（binding 2）+ 实例 SSBO（binding 3，仿射变换 3×vec4 + poseRow/tint）——零逐实例上传
- **直接 GL 绘制**：每帧每 program 一次 1 实例 raylib priming（NVIDIA GL 驱动按首次调用模式特化 program，实验锁定），其余全部 glDrawElementsInstanced
- **能力门**：Gl43.Initialize 探针——ARB 扩展 + 最小 430 着色器实测编译，fail-closed
- **GPU 计时**：gpuSkinPoseGpu/gpuSkinMainGpu/gpuSkinShadowGpu 三列（glQueryCounter 延迟回读）
- **旧路径退休**：RaylibPoseTexturePalette + pose_texture 着色器 + 7 个纹理测试全删；SSBO 合同测试改写

## spawn 断言根因（main 存量缺陷修复）
`SetGameplayTagContainer`（tags 非空时）派生 Add `TagCountContainer`；模板又显式声明 `"TagCountContainer": {}` → EntityBuilder 模板循环二次 Add → Arch 断言。修正 8 处模板 + ComponentRegistry 合同错误转正。

## 验收
| 场景 | 结果 |
|------|------|
| crowd_anim 10K（画廊） | 180 帧 avgFrame **18.3ms**（SSBO 直绘；原纹理路径 20.0ms，快 8.5%） |
| mass-nav 10K 全阴影 | 720 帧 exit=0，画面结构与 GPT 纹理基线等价（24937 vs 24882 竖直边缘） |
| gpuSkinDraw | 0.05ms（原 0.35ms CPU 提交时间） |
| gpuSkinBuild | 5.03ms（原 5.54ms；CPU 姿势行写入只剩 SSBO staging 复制） |
| 测试 | RaylibAdapterTests 312/312 绿；PresentationTests 1103/1128（25 失败全是 GPT 在途 LOD 特性破坏的既有测试，与本管线无关） |

## 关键发现
1. **raylib Matrix 字段按列分组声明**（m0,m4,m8,m12,…）——骨骼矩阵（GL 列=m0,m1,m2）与 FromSystemNumerics（GL 列=m0,m4,m8,m12）两种生产者约定并存
2. **索引网格绘制数=triangleCount*3**（rlgl 合同）
3. **NVIDIA GL 驱动对 SSBO program 按首次调用模式特化**：直绘前须一次 raylib DrawMeshInstanced priming
4. **NuGet 锁文件静默致构建失败**：`%TEMP%\NuGetScratch\lock\*` 残留 → 构建报错但 exit code 可能仍 0 → 跑旧二进制 → 调试方向全错。删锁文件即恢复
