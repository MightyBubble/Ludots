# MassNavigation 10K → 80 FPS 目标分析（工作稿）

基线：`origin/main` = `85dfe3529f`（已含 PR #1485 全量：`ffd11358f7` 合并点）。
工作树：`.worktrees/pi-1485-perf`，分支 `perf/massnav-10k-80fps`。
载体：`launcher.mass-navigation-10k-hud.runtime.json`（10K agents + AgentBridgeMod）。

## 1. 现场数据（Release，900/600 帧，诊断日志）

Average FPS ≈ 23（main），最好帧 57 FPS，最坏帧 5 FPS。目标 80 FPS ⇒ 帧预算 12.5ms。

单帧预算拆分（中位）：

| 区间 | 现值 | 目标 |
|---|---|---|
| `tick`（sim+presentation） | 20ms | ≤ 9ms |
| `presentation` | 13–20ms | ≤ 5ms |
| `sim` | 0.2–27ms（尖刺） | ≤ 4ms |
| `post`/`begin`/`endDraw`/GPU | ~1.5ms | 不变 |

## 2. 根因清单（按单帧时间排序）

### P0-1 `MassNavigationSimulationStepSystem` steering 5–29ms + hard resolve 3–12ms
- `steering`：10K agent × SonarSolver2D 全量求解，每帧。`_parallelWorkerCount=8` 已并行，但仍是 5–29ms。
- `hard`：`hardPairs=78041..189728/帧`，`hardPenetrating=12026..41579/帧`——15–22% 配对每帧被判穿透并 `SeparateAgents`。
  穿透率长期不收敛说明分离速度/顺序有缺陷，不只是计算量问题。
- 证据：`massnav target=.. steering=.. step=.. hard=.. hardPenetrating=..` 诊断行。

### P0-2 `CameraCullingSystem` 3.6–6.2ms/帧
- 动态实体每帧全量 `ProcessEntity`：spatial gate + loaded chunk gate + `ComputeScreenCoverageAndViewportIntersection` + LOD。
- 无「相机静止且实体未动 ⇒ 跳过」的 dirty 闸门（静态实体有 `PresentationStaticTransform`+`CullEpoch`，动态实体没有等价物）。

### P1-1 `MinimapPresentationSystem` 1.6–4.1ms/帧
- `MinimapRuntime.Refresh` 每帧全量投影 10009 个 marker（`ProjectMarkers`），无 revision 闸门。
- 视图未变（相机/缩放/标记未变）时本可复用上一帧结果。

### P1-2 `PresenterEntityTransformSyncSystem` 1.4–8.8ms/帧
- 10K owner payload 全量同步；PR #1485 已加 `positionOnly`/`SyncOwnerPayloadAttachedChildren` 优化。

### P1-3 `PresenterEmitSystem` 1.3–5.4ms/帧
- PR #1485 已加 retained world HUD fast path + projection 复用。

### P1-4 `hudProj`（WorldHudToScreenSystem）
- main 的 `85dfe3529f` 已把地形遮挡缓存化（8–10ms → 1.3–2.5ms）。

### P2 `worldHud=20000`（main 比 PR head 多）
- `85dfe3529f` 之后 HUD 项从 4898/8246 涨到 20000，需确认是否有回归。

## 3. 重复实现 / 结构债（用户点名的「傻逼代码」）

- `CameraCullingSystem`：40 个 `QueryDescription` + 20+ 个近乎复制粘贴的 `ProcessStatic*`/`ProcessVisual*`。`ProcessNoVisual` 里 `World.TryGet` 逐实体取 bounds/lod（本可用于 chunk span）。
- `WorldHudToScreenSystem`：owner 可见性缓存与 owner 投影缓存是两套平行 `Entity.Id+1` 数组 + 各自的 resize 逻辑，未复用。
- `MinimapRuntime` / `MinimapScreenMarkerBuffer`：style bucket key 与 stableId 双重建模。

## 4. 计划

1. 先做可测量、低风险的闸门优化（cull 动态 dirty、minimap revision 复用）。
2. 再做 MassNav steering/hard-resolve 的算法级优化（分离收敛、候选门控、邻域缓存）。
3. 每步用同一载体 + `LUDOTS_RAYLIB_TIMING_LOG_INTERVAL_FRAMES` 采 21+ 样本，记录 before/after。
4. 修掉 main 上 `worldHud=20000` 的疑似回归。
