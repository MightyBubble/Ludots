# W1a #1323 复核请求：帧执行单一化（FrameRenderer 成为唯一生产路径）

仓库当前分支 codex/eng0-native-resource-ledger（W0 已有 PR #1338），工作树的未提交改动是 #1323 W1a。看 `git diff -- src/`（不含 tmp/），核心三文件：
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/Rendering/RaylibFrameRenderer.cs`（重写：枚举+计划+执行合一）
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`（-413 行：内联帧序删除，改为构建 RaylibRenderFrame 记录调用 RenderFrame；删除了与 FrameRenderer 重复的 BeginCoreMode3D/EndCoreMode3D/Restore3DDepthState/MultMatrix/DrawInfiniteGrid/TerrainSourceFor）
- 测试：`RaylibFrameRendererTests.cs`（顺序不变量矩阵）、`RaylibNavMeshPresentationContractTests.cs` 与 `RaylibTerrainRendererTests.cs`（源码契约断言随迁移改指向 FrameRenderer）

## 设计（请审查而非复述）

1. `RaylibFrameRenderer` 长出水面双 pass（反射/折射，含 AbsoluteColor 覆盖、RenderTerrainOnly 分支、EnsureRenderTargets/Advance(dt)）、NavMesh overlay（含元数据文本）、TrailMeshes pass（生产宿主此前从不画 TrailMeshBuffer——单一化采纳了 FrameRenderer 侧的既有意图，属收敛修复，会在 PR 说明）。帧级环境准备（EnsureActiveForMap×3、frameLighting、ApplyFrameLighting×3、clear color）迁入 PrepareFrameEnvironment。
2. `RenderFrame` 现在先 `BuildPassPlan`（stackalloc）再按计划逐项 switch 执行——声明顺序=执行顺序由结构保证；`LastExecutedPasses` 零分配记录实际轨迹。BeginWorldTexture/PostProcessComposite 只在 UsePostProcess 时存在；水面 FBO 与后处理 RT 互斥语义保持（postProcessWorldFrame = !waterFboEnabled）。
3. 计划输入语义变化：HasGroundOverlays/HasSplineRibbons/HasTrailMeshes 恒 true（pass 内按 buffer 计数门控）；HasGlobalFieldBuffer/HasBenchmarkRenderer 按依赖存在性；DrawEnvironment = skyEnvironment.IsActive（在 PrepareFrameEnvironment 解析后取值）。
4. EndDrawing 仍在宿主（RenderFrame 只做 BeginDrawing..OverlayComposite；诊断 HUD 与截图取证留给 #1325 拆分）。异常安全：finally 兜底 EndMode3D/AbortWorldFrame/EndDrawing 保持。
5. TerrainSource 缓存语义保留（按 VertexMap 引用身份重建），从 HostLoop 静态迁为实例字段。

## 已有证据

- RaylibAdapterTests 全量 203/203（含 5 个新顺序/互斥/矩阵测试，>120 组合）。
- 真实宿主 E2E：launcher 启动 RaylibVisualAtmosphereShowcaseMod（WaterEnabled=true，走水面双 pass）与 PresenterBlacksmithShowcaseMod（图元+UI），LUDOTS_TAKE_SCREENSHOT_FRAME=90 + AUTO_EXIT 干净退出、截图产出（2.68MB / 1.16MB）。
- 截图校验在本机报 1600x900 vs 1280x720 尺寸错——已用基线二进制（stash 本改动后重建）复现同样报错，证明是本机 125% DPI 的预存环境问题，非本改动引入。

## 请重点审查

1. 迁移保真：HostLoop 原内联序列（我读的 :584-961）与 FrameRenderer 新执行的逐 pass 对应关系——有没有我搬丢/搬错顺序的行为（尤其水面 pass 内 sky/heightmap/terrain 分支、Terrain 的 AbsoluteColor 与 Bind/ClearReflectiveWater 时机、primitive 的 BindStampHeightSampleSource）。
2. 计划输入语义变化（恒 true 门控移入 pass 内）是否会让诊断/漂移测试失去信号。
3. `_lastExecutedPasses` 轨迹与异常路径（水面 pass 中途异常时 matrix stack 状态）。
4. HostLoop 删除的成员是否确实无引用（我已 grep+编译验证，请复核）。
5. 线程/时序：RenderFrame 内 PrepareFrameEnvironment 把 EnsureActiveForMap 等从宿主循环线程原样迁入，无跨线程变化——确认无隐藏假设。

输出：阻断/应改/通过三档，每条 file:line。用中文。
