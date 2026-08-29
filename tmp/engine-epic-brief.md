# 引擎工程化 Epic 方案评审请求（Ludots raylib 最小验收引擎）

你是被邀请来评审方案的工程评审人。请基于本仓库实际代码验证/反驳下列论断，并对路线优先级给出明确意见。仓库根：当前目录（遵守 AGENTS.md：六边形架构、一切皆 Mod、禁 fallback/向后兼容/重复造轮子/跨职责）。忽略 .pi-worktrees/、bin/、obj/。

## 背景

raylib 自研引擎作为 Ludots 的最小验收引擎，需要评估工程化现状，并规划向商业引擎（Unity/Unreal/Godot）看齐的路线：资源加载、内存管理、GPU 等。已完成一轮全面代码调查，结论如下。

## 现状论断（均有 file:line 证据，欢迎复核）

### 结构分层（评 4/5）
- 依赖方向干净且被 `src/Tests/ArchitectureTests/CoreBoundaryTests.cs:13-53` 机器强制；`Ludots.Raylib.Render` 零 Core 依赖；Gallery/AssetAcceptance 两 app 只拿渲染库。
- 债务1：`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`（2450 行 internal static class）god class：装配/输入路由/约 880 行单方法帧循环/截图证据采集与画面平坦度验收校验/点阵字形诊断 HUD/static 可变状态，12 类职责。
- 债务2：帧序双份维护。`Rendering/RaylibFrameRenderer.cs`（592 行，带声明式 BuildPassPlan）生产代码零实例化，只有测试引用；真正执行的序列内联在 HostLoop，两份已漂移（HostLoop 有水面双 pass 和 NavMesh，FrameRenderer 没有）。

### GPU/渲染（评 2.5/5）
- 强项：ISM 静态合批 + typed instanced lane + GPU 蒙皮实例化（按 meshAssetId/materialId/clipIndex/frameIndex 分桶），2128 行契约测试；split-sum IBL；水面反射折射双 RT；Skia 与 GL 同上下文零拷贝合成（`RaylibSkiaGpuOverlaySurface.cs:150-166` 把 raylib FBO 包装成 GRBackendRenderTarget）。
- 缺口1：无 RHI 抽象/render graph，pass 硬编码 if 链。
- 缺口2：实例矩阵不驻留 GPU，每次 DrawMeshInstanced 从 CPU 裸指针上传（`RaylibPrimitiveRenderer.cs:2337`）；全仓库无 rlUpdateBuffer/持久 VB/SSBO/indirect draw/compute。
- 缺口3：阴影能力存在（2048 单级正交，`RaylibDirectionalShadowMap.cs`，画廊在用）但主游戏宿主 RaylibHostLoop 中 "shadow" 零命中——未接线；而文档 `gitbook/architecture/raylib-render-code-shape.md` 声称宿主已组阴影帧（文档不实）。
- 缺口4：无 frustum culling（仅相机半径窗口 + 地形两档简化）、无逐实例剔除、无网格 LOD。
- 缺口5：单方向光、无点/聚光、无 MSAA/动态分辨率、后处理仅一个调色 pass 且与水面 FBO 互斥。

### 内存（评 3.5/5）
- 强项：每帧热路径零分配纪律好且有测试断言+artifacts 实测双证据（10k HUD 0.0 B/帧、90k timer 0-alloc 断言）；固定容量+drop 计数预算化数据面贯穿 HUD/请求/效果队列。
- 缺口1：模型/贴图缓存只进不出，无 LRU/逐出/显存预算（唯一亮点：地形 chunk 240 帧逐出，`RaylibTerrainRenderer.cs:512-531`）。
- 缺口2：native 内存不可观测——无 GC.AddMemoryPressure、无进程/VRAM 计量。
- 缺口3：GC 零配置（无 ServerGC/堆上限）。
- 缺口4：alloc 指标大多只入报告不设阈值，CI 守不住回归。

### 资产加载（评 2/5）
- 强项：三层 JSON 声明链（mesh_assets.json 句柄 → host_assets.json 后端绑定 → mod VFS URI 防越界）；Assimp→GLB 转换带 SHA1 磁盘缓存；Dispose 所有权纪律精细；#1050 OBJ 崩溃有回归测试。
- 缺口1：全同步、主线程、首次绘制懒加载（`RaylibPrimitiveRenderer.cs:1616-1671`），大模型首帧卡顿；无异步句柄模型。
- 缺口2：无引用计数、无运行时逐资产卸载、无全局去重——同 URI 多 lane 多份 GPU 拷贝。
- 缺口3：无热重载；负缓存导致文件补上后须重启。
- 缺口4：无贴图压缩/mipmap/图集/mesh LOD 离线管线（GenTextureMipmaps 全引擎 0 调用）。
- 缺口5：错误策略分裂：材质/skinned fail-loud vs mesh/billboard Warn+跳过；schema enum 漏 Sound 且无运行时校验。

## 提案路线（待你评审）

- 第 1 步（结构债）：RaylibFrameRenderer 变成唯一被执行的帧组织者，HostLoop 内联序列迁入（含水面双 pass/NavMesh），并拆出输入路由、证据采集、诊断 HUD。
- 第 2 步（资产）：统一资产管理器——AssetHandle<T> + 引用计数 + 按 URI 全局去重 + 异步加载状态机（worker 准备数据、GL 线程提交）；统一错误策略；LRU+字节预算可后置但引用计数卸载先行。
- 第 3 步（GPU 驻留与剔除）：实例矩阵持久 VB + rlUpdateBuffer 增量上传；instanced lane 整批 AABB frustum culling 起步；阴影 pass 接进主宿主；修正 raylib-render-code-shape.md 不实声称。
- 第 4 步（内存治理）：native 资源创建处补 GC.AddMemoryPressure；ServerGC+堆上限配置；benchmark 报告加 alloc 阈值断言。
- 暂缓：RHI 抽象/render graph、贴图压缩/图集、MSAA/TAA/bloom——定位仍是最小验收引擎，多后端/发行级画质需求未到。

## 请回答（每条给出代码依据或明确说"无法验证"）

1. 优先级排序是否成立？第 1 步作为地基是否正确，还是有更前置的债？
2. 各步骤有没有遗漏的结构性风险（例如：AssetManager 该放在哪一层、是否违反"渲染库零 Core"边界；持久 VB 与 raylib rlgl 层的兼容风险；HostLoop 拆分对截图证据链/合成输入回放的破坏面）？
3. 暂缓清单是否正确？有没有其实很便宜、应该提前的项？
4. 这个 epic 应拆成哪些子 issue（给清单：标题+验收标准要点）？
5. 明确给出你不同意的点（若有），说明理由。
