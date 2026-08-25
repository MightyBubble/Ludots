# Tech Debt Report: 2608-nav-runtime-bake-livelock

Date: 2026-08-23
Reporter: NavGate showcase 桥接验收（navmesh-413-debug-slice 分支，AgentBridge 实机会话）
Owner: nav 图能力线（issue #413 跟进）
Severity: P0（活锁）+ P1（同线程长阻塞）
Scope: Cross-layer（Core 导航 → raylib 宿主循环 → showcase 可玩性）

## Trigger

- Scenario: `nav_gate_valley` 地图（Grid 1×1 宏瓦、TerrainHeightStepCm=25、recast、runtime-incremental、includeNeighborTiles=true）上，NavGate 自动巡演反复落门/抬门圆形障碍（r=1100cm @ 3600,3600）。
- Entry point: `RuntimeNavMeshObstacleDirtySystem.Update` → `RuntimeIncrementalNavMeshRebuildQueue.ProcessBudget` → `NavBakeService.Bake` → `RecastNavTileBaker.TryBake`（游戏线程内同步执行）。
- Repro steps: 以 `launcher.nav-gate-bridge.runtime.json` 启动实机，放任自动巡演运行约 15 分钟（第 6 个完整落门循环的抬门重烤期间必现概率高；前 5 个循环每次也伴随可感知的帧冻结）。

## Evidence

- 活锁栈（`dotnet-stack report` 抓取于卡死进程，主线程 100% 单核自旋、窗口未响应、bridge 全部工具超时）：

  ```
  RcMeshDetails.DistPtTri / DistToTriMesh / BuildPolyDetail / BuildPolyMeshDetail
  RcBuilder.Build
  RecastNavTileBaker.TryBake
  NavBakeService.Bake
  RuntimeIncrementalNavMeshRebuildQueue.ProcessBudget(int32)
  RuntimeNavMeshObstacleDirtySystem.Update
  GameEngine.Tick → RaylibHostLoop.Run
  ```

- 完整巡演时间线：`/tmp/navgate-run.log`（12 次 reason=timeline 落/抬门后，第 6 次抬门重烤期间日志停止增长）。
- 单瓦输入规模：`mods/LudotsCoreMod/assets/Data/Nav/nav_gate_valley/.../artifact_0_0.json`（输入 11432 三角、输出 874）——expanded 邻区输入使每次重烤都是秒级工作量。
- 卡死进程现场：PID 80532，CPU 持续爬升（~100% 单核）、Responding=False。
- 源码：
  - `src/Core/Ludots.Physics2D/Systems/RuntimeNavMeshObstacleDirtySystem.cs`（Update 内同步 ProcessBudget，第 70-73 行）
  - `src/Core/Navigation/NavMesh/Bake/RuntimeIncrementalNavMeshRebuildQueue.cs`（预算按“瓦片数”切片，单瓦 TryBake 无时间上限）
  - `src/Core/Navigation/NavMesh/Bake/RecastNavTileBaker.cs`（BuildExpandedRecastTriangleMesh：邻区扩展输入；BuildRcConfig：detailSampleDist=6）
- 相关 showcase 合同（头less 单循环通过，未覆盖持续循环）：`src/Tests/GasTests/Map/NavGateShowcaseContractTests.cs`

## Impact

- User-visible impact: 每次落门/抬门游戏冻结数秒至数十秒（含邻区瓦片放大，单 tick 最多烘焙 tileBudgetPerFixedTick=4 瓦）；持续运行约 6 个循环后游戏整体活锁，只能杀进程。
- Correctness/stability risk: 任何 runtime-incremental 地图上的持续障碍变更都会复现；活锁期间 bridge/输入/渲染全停，等同崩溃。
- Blast radius: Core 导航管线 → 所有 runtime 障碍使用者（NavGate showcase、后续 RTS 单位建筑/工事）→ raylib 桌面宿主。

## Fuse Decision

- Mode: explicit-degrade（showcase 层）
- Reason: 引擎级修复（烘焙移出游戏线程 + 单瓦时限）是基建任务，不在 showcase 分支内抢做；showcase 用“自动落门循环上限 + 显式日志”把持续暴露降到安全水位，保留全部演示与交互（手动 G/F/N/P/O/R/T 不受限）。
- Observability fields: `[NavGate] 稳定性熔断 NAV-R2：自动落门已满 N 圈...`（控制台 + tour 阶段日志）；`NavGateIds.MaxAutoGateCycles`；techdebt 本文件。

## Containment and Follow-up

- Immediate containment（本分支已落地）:
  1. **根因调优（活锁消除）**：`RecastNavTileBaker.BuildRcConfig` 的
     `detailSampleDist 6→16 / detailSampleMaxError 1→4`——原参数下
     `BuildPolyDetail` 的逐样例插入循环在大多边形上呈平方级膨胀（误差阈值 1ch≈8cm
     在量化地形上永难收敛 → 全样例迭代 × 每轮全量 DelaunayHull 重建）。
     调优后实机 13 分钟连续巡演（5 次落门 + 手动消融）无卡死，合同测试全绿。
     注意 `detailSampleDist=0` 不可用：部分多边形 detail 为空导致 Detour 序列化越界；
  2. NavGate 自动巡演落门上限 3 圈（`NavGateIds.MaxAutoGateCycles`），达到后显式提示并停用自动落门；
  3. 首程行军 5 秒兜底强制落门（`MarchGateFallbackTicks`），保证惊喜时刻在第一圈出现（离线瓦片与运行时重烤的路径布线差异曾导致前 4 圈静默通过）；
  4. 手动交互（G 落/抬门等）保留，README 风险区明示持续手动循环同样可能触发长阻塞。
- Permanent fix direction（引擎层，单独排期）:
  1. 重烤移出游戏线程：后台 worker 逐瓦烘焙 + 主线程提交（需处理 NavTileStore 单写者断言的跨线程提交点）；
  2. 单瓦烘焙时限看门狗：超时瓦片放弃本次烘焙（保留旧瓦片）并计数上报，杜绝 DotRecast `BuildPolyDetail` 病态输入的无限自旋；
  3. 落门重烤输入瘦身：障碍事件只重烤直接相交瓦片，邻区一致性走焊接校正而非全量重烤；
  4. 抬门走“干净瓦片快照恢复”而非从地形全量重烤（Unity carve/restore 对齐做法）。
- Target milestone: nav 图能力线下一个迭代（对齐 `gitbook/architecture/graph-capability-status.md` 的运行时烘焙条目）。
