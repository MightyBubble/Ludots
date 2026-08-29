# W2 #1326 复核请求：能力接线修复包（阴影/水面-后处理互斥/schema/文档合同）

分支 codex/eng1a-frame-single-execution（W1a 已提交），工作树未提交改动是 W2。看 `git diff`（相对 HEAD），涉及：
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/Rendering/RaylibFrameRenderer.cs`：新增 ShadowDepth pass（RaylibDirectionalShadowMap 惰性构造、BeginFrame(sunDir, camera.target, 48m)→DrawShadow(即时+蒙皮车道)→EndFrame，try/finally 成对收尾）；ApplyFrameLighting 三处传入 frameShadow；**后处理/水面互斥修正**：BuildPassPlan 里 BeginWorldTexture 挪到水面与阴影 pass 之后（根因：水面 EndTextureMode 切回默认帧缓冲会丢后处理 RT 绑定——注释已写明）；UsePostProcess 改为只由环境配置驱动，不再与水面互斥
- `src/Core/Presentation/Hud/RenderDebugState.cs`：新增 DrawShadows（默认 true）
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`：frameRenderer 声明外提 + finally 释放（阴影 RT 生命周期）
- `src/Tests/RaylibAdapterTests/RaylibFrameRendererTests.cs`：水面+后处理共存断言、阴影顺序不变量、矩阵扩到 >500 组合
- `assets/Presentation/host_assets.schema.json`：assetKind 补 Sound（运行时 loader 已支持且未知 kind 抛错——两边对齐）
- `gitbook/architecture/raylib-render-code-shape.md`：帧内数据流改为新现实（唯一帧执行者/阴影 pass/互斥根因），新增"资产加载错误策略合同"章节（fail-loud 定稿、负缓存失效语义、偏差收敛指向 #1327）
- `gitbook/architecture/gpu-skinned-instancing-and-offline-retarget.md`：FBX 声称改为运行时转换现实

## 已有证据
- adapter 全量 203/203；真实宿主 E2E 两 mod 截图产出正常。
- 视觉回归（W1a 后 vs W2）双审核正在跑，结论会补到 PR。

## 请重点审查
1. 阴影接线的正确性：ShadowSceneRadiusMeters=48 硬编码 + camera.target 为中心的包围盒选择是否会在大地图失真（阴影只覆盖相机附近 48m 半径——文档/后续是否需要配置化）；DrawShadow 对即时/蒙皮/lane 三类投射物的覆盖是否完整；夜间（太阳低于地平线重映射为月光）阴影方向是否自然。
2. 互斥修正的 GL 状态安全：水面 pass 后再 BeginWorldFrame(RT) 的目标切换顺序，与 PostProcessRenderer.BeginWorldFrame 的内部假设是否冲突（读 RaylibPostProcessRenderer 核对）；无水面时顺序变化（RT 开启时机后移）是否影响任何依赖。
3. 阴影 RT 的创建时机（惰性、首帧在 PrepareFrameEnvironment 内、BeginDrawing 之后）与释放（HostLoop finally）是否完整。
4. RenderDebugState 加字段的兼容性（Core 类型，消费方是否都默认安全）。
5. schema/文档改动的事实准确性。

输出：阻断/应改/通过三档，每条 file:line。用中文。
