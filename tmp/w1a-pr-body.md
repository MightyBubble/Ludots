Part of #1321 · 实现 #1323（W1a 帧执行单一化，epic 最重的单一动作）

** stacked on #1338（含 W0 两个提交，#1338 合并后本 PR diff 即为纯 W1a）**

## 改了什么

- **`RaylibFrameRenderer` 长成完整帧执行者**：水面反射/折射双 pass（含 AbsoluteColor 覆盖、RenderTerrainOnly 分支、EnsureRenderTargets/Advance(dt)，各 pass 独立 try/finally 成对收尾 3D 模式与水面目标）、NavMesh overlay（含元数据文本写入 screenOverlayBuffer）、TrailMeshes pass、后处理 RT（BeginWorldTexture/PostProcessComposite 与水面 FBO 互斥语义保持）、帧级环境准备（EnsureActiveForMap×3、frameLighting、ApplyFrameLighting×3、clear color）。
- **声明=执行**：`RenderFrame` 先 `BuildPassPlan`（stackalloc）再按计划逐项 switch 执行——顺序一致性由结构保证，不再存在两份帧序。`LastExecutedPasses` 在入口清零、按"已进入"记录，供诊断与漂移检查。计划层省略的可选 pass 每帧补零观测，保持旧宿主的诊断序列语义。
- **HostLoop 减重 413 行**：350 行内联帧序替换为构建 `RaylibRenderFrame` 记录 + 一次调用；删除与 FrameRenderer 重复的 BeginCoreMode3D/EndCoreMode3D/Restore3DDepthState/MultMatrix/DrawInfiniteGrid/TerrainSourceFor。EndDrawing、诊断 HUD、截图取证按 #1325 计划仍留宿主。
- **收敛修复（有意的行为新增，单独列出）**：生产宿主此前从不绘制 `TrailMeshBuffer`（两份帧序漂移的另一面）；单一化采纳 FrameRenderer 侧既有意图，主宿主地图中的 trail presenter 现在会真正渲染。
- **测试**：顺序契约矩阵测试（>120 开关组合：首尾锚点、无重复、水面双 pass 相邻且在 BeginWorld3D 前、NavMesh 在 Terrain 与 GlobalField 之间、RT/水面互斥、UI 层序）；两个源码契约测试随迁移改指 FrameRenderer，负向断言保持盯宿主组装层。

## 验证证据

- RaylibAdapterTests 全量 203/203。
- 真实宿主 E2E：launcher 启动 `RaylibVisualAtmosphereShowcaseMod`（WaterEnabled=true，走水面双 pass）与 `PresenterBlacksmithShowcaseMod`（图元+UI），`LUDOTS_TAKE_SCREENSHOT_FRAME=90`+`AUTO_EXIT` 干净退出，截图 2.69MB / 1.16MB（宿主内置平坦度校验通过）。
- 本机截图尺寸报错（1600×900 vs 1280×720）：已用**基线二进制**（stash 本改动后重建 Release）复现同样报错——本机 125% DPI 的预存环境问题，非本改动引入。

## 复核记录

**codex（gpt-5.6-sol）**：1 阻断 + 3 应改，已全部修复——水面 pass 异常清理不完整（补 try/finally）、执行轨迹合同不清（入口清零+文档"已进入"）、计划省略 pass 时诊断不归零（补零观测）、负向断言被改到错误文件（回退到宿主源码）。其逐 pass 对照确认：水面双 pass 内 sky/高度图/VertexMap 分支、AbsoluteColor、BindStampHeightSampleSource、总体 pass 顺序与迁移前一致。
**pi（claude opus）**：4 次尝试（两种提示词、@文件直供、限定只读两文件）均超时或静默无输出，未能交付；最小连通性检查正常，判断为环境/API 侧问题。已在 issue 记录，W1a 的逐 pass 保真核对以 codex 的独立对照为准。

Closes #1323
