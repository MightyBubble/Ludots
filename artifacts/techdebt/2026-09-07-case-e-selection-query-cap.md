# Tech Debt Report: 2026-09-07-case-e-selection-query-cap

Date: 2026-09-07
Reporter: pi（#1398 任务2 刀3/刀4 实施；Case E 10k 压力实测触出）
Owner: Core — GASGraph VM / EntityQueries（图能力线）
Severity: **P1**
Scope: **Cross-layer**（图 VM 寄存器模型 ↔ 地图查询语义 ↔ 展示层候选集）

## Trigger

- 场景：Case E 框选候选集（`case_e.selectable`）在 **10k 实体 / 100 可选单位** 世界下的完整性实测。
- Entry point：`graph.case_e.roster_sync`（EntitySpawned/EntityDied → `QueryAllMapEntities` → `QueryFilterTeam` → `QueryFilterTemplate` → `WriteCollection replace`）。
- Repro：`CaseESelectionScalePressureTests.BoxSelectionChain_At100PlayersAnd10kEntities_StaysWithinBudget`（headless，10k 世界实体，100 支可选 marine）。**世界内 104 支可选，候选集只进 77 支。**

## Evidence

- `src/Tests/GasTests/Production/CaseESelectionScalePressureTests.cs`（可复现 + 钉住截断行为 + 耗实测）
- `src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs`（`MaxTargets = 256`）
- `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`（`HandleQueryAllMapEntities` → `CollectMapEntities(s.Targets)`，定长切片）
- 实测数据：`docs/benchmarks/case-e-selection-scale/case-e-selection-scale.csv` / `RUNS.md`
- 现有正确性覆盖：`CaseESelectionShowcaseAcceptanceTests`（拖拽中候选死亡、入/出/再入框、同拍结算）

## Impact

- User-visible：80+ 单位的可框选阵容在大地图上框选时，**候选集只覆盖前 256 个 MapEntity 查得着的单位**，框选会漏人，且漏人随地图实体布局/创建序漂移。
- Correctness/stability risk：**静默截断**（无报错、无计数警示），候选集 = f(前 256 查询序)，语义上不成立。
- Blast radius：任何依赖 `QueryAllMapEntities` 聚合/罗列的图（roster、人口统计、编队）都会在人口 >256 时失真；Case E 只是第一个暴露者。

## 根因拆两条（一条可修缺陷、一条设计债）

1. **定长寄存器 vs ECS 流式查询（硬截断）**：图 VM 把实体集塞进固定 `MaxTargets=256` 的 TargetList 寄存器，`QueryAllMapEntities` 因此天然只取前 256。ECS 侧查询是流式/分块的，两者接口不匹配——这是「query graph 单开 GraphKind 走专属编译管线适配 ECS 高性能过滤」方向上落地的具体裂缝。
2. **候选集维护原语缺位（做法要变）**：roster 是「**事件驱动的全量重建**」——每出生/死亡事件 O(N) 重扫全图；生命周期内的过滤条件突变（team / 模板 / 状态）没有刷新源（`ScreenRegionToEntities` 只做几何命中、不重查过滤条件），候选集会在该场景下过期。10k 单波 burst 还会退化为当拍 O(N²)。正确做法是**增量成员维护**（出生 add / 死亡 remove / 条件突变 diff）+ 解除 256 顶（分页 / 流式收集），而不是继续堆全量重建。

## Fuse Decision

- Mode：**explicit-degrade**（隔离到既有 showcase 规模）：在 256 顶被解除前，候选集完整性不得声明为「10k/100 玩家达标」；`CaseESelectionScalePressureTests` 将截断行为与耗时钉为可见观测（断言 `roster <= 256`），`RUNS.md` 明示该顶，避免静默回归。
- Reason：P1 缺陷影响 80+ 单位阵容的正确性，但当前 PR（刀1~刀4）业务规模（<20 单位）不受影响，且解除 256 顶属 Core 图 VM 枢纽改动，捆进本 PR 会放大评审面。
- Observability：测试断言行（`候选集 <= MaxTargets(256)`）+ CSV 列 `roster_count/world_marines` + `RUNS.md` 发现段。

## Containment and Follow-up

- Immediate containment：本 PR 不改 VM；压力实测把行为钉住、文档明示；正确性缺口测试补到「拖拽中候选死亡 / 再入框」级。
- Permanent fix direction：
  1. （可修）`QueryAllMapEntities` / 全图聚合查询支持分页或流式收集，突破 `MaxTargets=256` 定长寄存器模型（先出方案：分页续传 or TargetList 扩容 or 查直接在 ECS 侧回落）。
  2. （做法要变）候选集从「事件驱动全量重建」迁到「增量成员 + 过滤条件 diff」原语；为 team/模板/状态突变提供刷新入口。
- Target milestone：图能力线下一轮（建议单独开票，绑定 `graph-capability-status.md` 3.3.1 账行）。
