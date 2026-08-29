Part of #1321 · 实现 #1331（W4c lane 内逐实例视锥剔除）+ #1332（W5 文档漂移清扫）

**stacked on #1377（含前序全部提交，合并后本 PR diff 即为纯 W4c+W5）**

## W4c：lane 内逐实例视锥剔除（#1331）

- **帧级视锥侧平面**：`Draw` 帧首由相机位姿+fovy 构建（System.Numerics 行向量约定下的列组合 `col1±col4 / col2±col4`）；只做**四侧平面**的保守球筛选——近平面（near=0.05 收益可忽略）与远平面不参与，深度约定差异（GL vs D3D）因此不构成风险；平面构建失败兜底为全可见（保守方向）。
- **提交点压缩**（主颜色 pass）：primitive lane 用单位立方半径（0.867m）、typed model lane 用缓存 AABB 对角半径 × 实例矩阵最大缩放轴。全可见时**零拷贝**直接用原批次；有剔除时压缩进复用 scratch 数组（稳态零分配）。`LastInstancedLaneCullSkippedCount` 诊断计数（仅主 pass）。
- **边界**：revision 矩阵缓存不受剔除影响（缓存全量、压缩每帧独立——不会因可见数恰好相同而错误命中上一帧集合）；**阴影 pass 不剔除**（光源视锥与主相机视锥不同，语义不适用）；实体级可见性（`lane.Visible`，源自 Core `CullState`）不重算——无第二套实体级 culling SSOT。

### 复核记录

- **codex（gpt-5.6-sol）**：3 阻断 + 2 应改，**全部成立并全部修复**——①平面提取矩阵约定错误（初版转置+行组合取错系数）→ 改列组合并只留侧平面；②剔除混入批次重建破坏 revision 缓存语义（可见数变化每帧 miss / 恰好相同错误命中）→ 剔除撤出重建、改提交点压缩；③阴影 pass 被主相机视锥误剔除 → 阴影路径不剔除；④固定半径 4m 不保守 → AABB 对角半径×实例缩放；⑤无用逆矩阵删除。正交投影参数序经推导确认正确。
- **pi（claude opus）视觉定量**：presenter 对零回归（max=2，实体质心位移 ≤2px）；大气对实体零误剔除（绿色植被面积差 -0.06%、沙地 -0.01%、岛屿轮廓完整无空洞、最大差异像素为同色系明暗变化而非回退背景色、差异热点全部在海面波浪带）。

## W5：文档漂移清扫（#1332，收完即关）

- `runtime-overview.md` SystemGroup 相位序列对齐 `ArchitectureGuardTests` 锁定的 **12 相**（补 `RuntimeEntityBinding` 与 `Continuation`）。
- W2 时代的其余漂移（阴影声称/FBX 声称/水面后处理互斥）已随 #1359 修正，本 PR 核验无残留。

## 验证证据

- adapter 全量 **225/225**；launcher 双 mod 冒烟正常；自差分 blacksmith max=2 零差异级。

Closes #1331
Closes #1332
