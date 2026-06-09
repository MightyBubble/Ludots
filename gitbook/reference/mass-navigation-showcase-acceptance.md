# Mass Navigation Showcase 验收指南

本文面向 0 上下文的技术同事和 Mod 开发者。

它回答一个很具体的问题：如果我要把 Ludots 的大世界导航能力接进自己的 Mod，我需要看到什么 showcase，才敢相信这套东西真的能工作。

本文不是底层算法设计文档，也不声明当前主线已经全部完成目标能力。它定义的是可玩、可观察、可复现的验收形态。

## 1. 一句话目标

Mod 开发者不应该通过阅读 Core 源码来建立信心。他应该打开一个 showcase，完成几次普通 RTS 操作，然后同时看到三类证据：

- 玩家视角：单位真的能在大世界里稳定移动、绕障、成队、到达。
- 开发者视角：路线、navmesh、flowfield、chunk streaming、性能预算都能被打开查看。
- UAT 视角：同一轮操作能产出 battle report、trace、截图、路径图和性能摘要。

只要这三类证据缺一类，showcase 就还不能让 Mod 开发者放心。

## 2. 先懂四个名词

| 名词 | 给 Mod 开发者的解释 |
| --- | --- |
| Road graph | 长距离路线网络。适合道路、桥、隘口、战略通道和低成本行军线。 |
| Navmesh | 近距离可行走区域。适合自由地形、最后一公里、绕开不可走区域。 |
| Flowfield | 大群体共享的方向场。适合 10k 级单位同时移动时避免每个单位都单独寻路。 |
| Chunk streaming | 大世界只加载当前需要的地图、导航和表现数据。64km 地图不能一次把所有热数据全塞进运行时。 |

从玩家操作上看，它们应该是一件事：框选单位，点目标，部队走过去。

从开发者观察上看，它们必须是四层证据：长距离路网、局部 navmesh、群体 flow、流送边界都能单独打开看。

## 3. 推荐的 Showcase 组合

最终应该有一个主 showcase，加几个聚焦 drilldown showcase。

| Showcase | 开发者看到什么 | 它证明什么 |
| --- | --- | --- |
| Large World Navigation Hub | 64km 地图、256x256 macro chunk 小地图、10k 单位、路网路线、navmesh corridor、flowfield overlay、性能 HUD | 证明整条链路能作为一个真实 Mod 入口运行 |
| Road Graph Corridor Showcase | 远距离下指令后，路线优先沿道路、桥和门户走，并显示 cost、profile、portal | 证明战略寻路不是每个单位临时乱跑 |
| NavMesh Bake and Query Showcase | 地形、blocked cause、walk mask、contours、polygons、portal、runtime query 可视化 | 证明 navmesh 资产可解释、可诊断、可回归 |
| Mass Crowd Flowfield Showcase | 10k 单位同屏移动，开启 flow、density、avoidance、stuck overlay | 证明大群体执行层可以承接真实订单 |
| Chunk Streaming Showcase | 相机和命令跨大世界移动时，loaded chunks/nav tiles/flow windows 随之变化 | 证明 64km 世界不是靠一次性加载硬撑 |
| Evidence Recorder Showcase | 一键录制 UAT，产出 report、trace、screens、path、perf summary | 证明问题能被复现、比较和归档 |

主 showcase 是给第一次接触的人看的。drilldown showcase 是出问题时给开发者定位原因用的。

## 4. 主 Showcase 第一屏应该长什么样

开发者第一次进入 `Large World Navigation Hub` 时，不需要知道任何内部类名，也应该立刻看懂当前世界状态。

屏幕上至少应该有这些东西：

- 主视图：大世界地形，10k 单位可见，静态障碍物可见或可抽样显示。
- 小地图：64km x 64km 边界、256x256 macro chunk 网格、相机位置、选中队伍、目标点、当前路线。
- 顶部性能条：FPS、frame p95、agent count、visible performers、loaded chunks、active nav tiles、active flow windows。
- 左侧场景预设：`10k move`、`road corridor`、`navmesh last mile`、`40k static obstacles`、`multi hotspot`、`record UAT`。
- 调试图层开关：route、navmesh、flow、density、obstacle、chunk、stuck、budget。

这些信息不是为了炫技，而是为了让 Mod 开发者知道：系统现在在用哪种策略、加载了哪些数据、有没有超预算、单位为什么这么走。

## 5. 必须通过的场景矩阵

### S1：64km 世界加载

操作：进入 showcase，打开小地图和 chunk overlay。

应该看到：

- 世界边界是 64km x 64km。
- macro chunk 网格是 256x256。
- 相机只激活附近 chunk，远处 chunk 不进入热路径。
- 世界边缘点击、相机移动和 ground picking 都不会越界。

通过标准：UAT 摘要记录 world size、macro chunk count、loaded chunk count 和边界点击结果。

### S2：远距离路网移动

操作：框选一支队伍，点击几十公里外目标。

应该看到：

- route overlay 先显示 road graph / portal corridor。
- 小地图显示完整战略路线。
- 主视图只展开当前活跃段，不把全程细节一次性铺开。
- 若目标不可达，面板给出明确 blocked reason，不静默失败。

通过标准：trace 中记录 planner profile、road edges、portal sequence、cost、route length、failure reason。

### S3：NavMesh 最后一公里

操作：把目标点放在建筑群、山地、瓶颈或障碍附近。

应该看到：

- 长距离仍走 road corridor。
- 接近目标后切到 navmesh corridor。
- navmesh overlay 能显示当前位置附近的 polygon、portal、blocked cause。
- 单位不会穿过不可走区域，也不会在目标附近大面积抖动。

通过标准：UAT 记录 corridor handoff 点、navmesh query 成功率、blocked query 样本。

### S4：10k 同屏群体移动

操作：在同屏 10k 单位下达移动、停止、改派、穿越瓶颈。

应该看到：

- 单位整体响应命令，而不是明显分批卡死。
- 队伍在瓶颈处形成拥堵、排队、分流，而不是穿模或爆散。
- 到达后能稳定停住，少量未到达单位有 stuck/retry 解释。
- FPS HUD 保持在目标预算内。

通过标准：记录 selected agents、moving agents、arrived agents、stuck agents、avoidance pairs、frame p95。

### S5：40k 静态障碍

操作：加载带 40k 静态障碍的场景，切换 obstacle overlay。

应该看到：

- 静态障碍作为 baked data 影响 road graph、navmesh 和 flow cost。
- 运行时不会把 40k 障碍全部当作动态 obstacle 每帧重建。
- obstacle overlay 支持抽样、分层、局部查看。

通过标准：UAT 摘要区分 baked static obstacle count 和 runtime dynamic obstacle count。

### S6：Flowfield 开启

操作：打开 flow overlay，让多个队伍朝不同目标移动。

应该看到：

- 每个活跃战区有自己的 flow window 或 flow tile set。
- flow 方向、density 和 obstacle cost 能叠加查看。
- 同一目标的大群体共享 flow，不出现每单位一条完整路径。
- 关闭 flow 后可对比 steering-only 行为，但默认验收必须覆盖 flow-on。

通过标准：记录 active flow windows、dirty tiles、frontier iterations、flow rebuild time、flow cache hit。

### S7：多热点大世界

操作：在地图多个远距离区域同时下达队伍命令，切换相机和小地图关注点。

应该看到：

- 当前镜头附近保持高频模拟和完整表现。
- 远处热点保持低频计划和必要状态，不假装满帧模拟。
- 切回远处热点时，路线、位置、到达状态连续，不发生重置。

通过标准：trace 中记录 hotspot id、simulation LOD、flow window 生命周期、chunk load/unload 事件。

### S8：诊断默认关闭

操作：关闭所有 debug overlay，只进行普通游玩。

应该看到：

- 没有大量日志刷屏。
- 没有每帧分配截图、JSON、字符串。
- 性能 HUD 可以保留轻量计数器，但详细诊断不进入 hot path。

通过标准：性能摘要包含 diagnostics mode、alloc budget、debug draw command count。

### S9：一键 UAT 录制

操作：点击 `Record UAT`，跑完预设路线。

应该产出：

- `battle-report.md`
- `summary.json`
- `trace.jsonl`
- `path.mmd`
- `screens/*.png`
- `timeline.png`
- `perf-summary.json`

通过标准：同一份 report 能回答“发生了什么、走了哪条路、为什么这么走、帧时间是否达标、失败时失败在哪一层”。

## 6. 性能口径

目标是玩家在 64km 概念世界中流畅体验 10k+ 同屏 agent 寻路避障和 40k+ 静态障碍物，80 FPS 稳定运行。

showcase 的性能口径必须写进 UAT，而不是只靠人工体感。

| 指标 | 绿色标准 |
| --- | --- |
| FPS | 稳定接近 80 FPS |
| frame p95 | 小于或等于 12.5ms |
| 10k agent move | 无明显停顿、爆散、长期卡死 |
| static obstacles | 40k baked static obstacles 不进入每帧动态重建 |
| diagnostics off | 无截图、无 trace 写盘、无 debug mesh 大量生成 |
| diagnostics on | 可视化抽样、分层、预算化，不把验收模式伪装成正常游玩性能 |

如果 showcase 只给出 headless 测试，不给真实 renderer frame time，不能算通过玩家体验验收。

## 7. Mod 开发者最关心的作者体验

一个可信 showcase 必须证明 Mod 开发者不需要改 Core 就能接入导航能力。

开发者应该能从 showcase 里学到：

- 地图如何声明世界尺寸、macro chunk、导航资产。
- 单位模板如何声明 agent profile、半径、速度、质量、队伍关系。
- order 如何表达移动意图，而不是把路线采样点塞进 order payload。
- road graph、navmesh、flowfield 的策略选择如何配置。
- 失败时如何拿到 blocked reason、profile mismatch、missing nav tile、unreachable portal。
- 如何打开 overlay 和录制 UAT。

如果接入一个新 Mod 需要复制 showcase 私有 runtime、复制 config loader、复制 minimap、复制 selection 或复制 performer 管线，这个 showcase 就没有证明 capability 可复用。

## 8. 哪些东西不够让人放心

以下内容可以作为中间证据，但不能作为最终验收：

- 只展示一张 10k 单位静态截图。
- 只跑 headless，不记录真实 renderer FPS。
- flowfield 默认关闭，却宣称完成 flowfield 避障验收。
- 只在 100m x 100m solver window 里通过，却宣称 64km 多热点通过。
- 静态障碍只在 UI 上显示，没有进入 navmesh、graph cost 或 flow cost。
- 远距离移动只画线，不证明 road graph 到 navmesh 到 mass execution 的 handoff。
- 诊断图层打开后严重拖慢，但没有标注它是 evidence mode。
- 失败时只有“没有路径”，没有 blocked cause 和策略层原因。

## 9. 当前仓库可复用基础

本页定义目标 showcase 形态。当前仓库中已经有几块可以复用的基础：

- Road graph / chunk streaming 方向：`mods/showcases/road_network/RoadNetworkShowcaseMod/`、`mods/showcases/chunk_streaming/ChunkStreamingShowcaseMod/`
- Navigation2D 可玩验收：`mods/Navigation2DPlaygroundMod/`
- Flowfield 基础：`src/Core/Navigation2D/FlowField/`
- Navmesh bake / query 基础：`src/Core/Navigation/NavMesh/`
- Evidence 产物链路：`src/Tools/Ludots.Launcher.Evidence/LauncherEvidenceRecorder.cs`

近期 mass navigation foundation 分支提供了更接近目标的 10k agent、command bridge、performer、minimap 和 diagnostics 链路。它适合作为主 showcase 的基础，但还必须补齐多 window flow、road graph/navmesh handoff、40k static obstacle baked evidence 和真实 80 FPS UAT，才能满足本文目标。

## 10. 最终验收句式

当这个 showcase 真正完成时，我们应该可以对一个 0 上下文 Mod 开发者这样说：

打开 Large World Navigation Hub。框选 10k 单位，点几十公里外的目标。你会看到小地图上的 road corridor、主视图里的 navmesh last-mile、flowfield 的局部方向场、chunk streaming 的加载窗口和实时性能预算。跑完后点 Record UAT，你会拿到同一轮操作的截图、trace、路线图和性能报告。你不需要读 Core 源码，也能判断这套导航能力是否适合接入你的 Mod。
