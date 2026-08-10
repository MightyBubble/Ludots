# RFC: EQS + Influence Map 空间推理体系

## 状态
草案 — 实施中（branch `codex/merge-ai-boundary-eqs`）

## 动机（第一性原理）

AI 决策需要回答两类空间问题：
1. **"哪里最好？"** — 从一组候选位置中，按多维度打分选最优（EQS）。
2. **"这片区域有多危险/有多少机会？"** — 连续标量场的投影与采样（Influence Map）。

现状：Utility AI 只能对**实体目标**打分（`DistanceToTarget`、`TargetPriorityBucket` 等），无法对**空间位置**推理。缺少 EQS 与 Influence Map。

## 复用优先（reuse-first）

绝不重造以下已有基建：

| 需求 | 复用的既有基建 | 位置 |
|------|--------------|------|
| 形状查询（cast 函数）| `ISpatialQueryService`（AABB/Radius/Cone/OBB/Line/HexRange/HexRing）| `src/Core/Spatial/` |
| 标量场存储 | `ChunkedField2D<float>`（分块 SoA，0-alloc warm path）| `src/Core/Fields/` |
| 密度 + 范围尺度 | `FieldGridSpec2D(cellSizeCm, chunkSizeCells)` | `src/Core/Fields/` |
| 节点候选来源 | transport network `ChunkedNodeGraphStore` | `src/Core/TransportNetwork/` |
| 网格/棋盘候选 | `BoardConfig` / `FieldGridSpec2D` | `src/Core/Map/Board/` |
| Utility 打分接入 | `UtilityAiInputKind` 扩展点 | `src/Core/Gameplay/AI/Utility/` |
| 确定性数学 | `WorldCmInt2`、`MathUtil.FloorDiv` | `src/Core/Math/` |

## 分层架构

```
┌─────────────────────────────────────────────────────────┐
│  Utility AI (Deliberation)                                │
│  UtilityAiInputKind.InfluenceSample01 / EqsBestScore01    │  ← 新接入点
└───────────────────────┬───────────────────────────────────┘
                        │ 只读采样
┌───────────────────────▼───────────────────────────────────┐
│  EQS 层 (Ludots.Core.Spatial.Eqs)                          │
│  ┌──────────────┐   ┌───────────────┐   ┌──────────────┐  │
│  │ Generator    │──▶│ Test/Score    │──▶│ Selection    │  │
│  │ Grid/Ring/   │   │ Distance/     │   │ Best/TopN/   │  │
│  │ Donut/Node/  │   │ Overlap(cast)/│   │ Threshold    │  │
│  │ Circle/Board │   │ Influence     │   │              │  │
│  └──────────────┘   └───────┬───────┘   └──────────────┘  │
└──────────────────────────────┼─────────────────────────────┘
              复用 cast 函数     │        采样 influence
        ┌──────────────────────┼──────────────────────┐
        ▼                       ▼                       ▼
┌────────────────┐   ┌────────────────────┐   ┌──────────────────┐
│ISpatialQuery   │   │Influence 层         │   │TransportNetwork  │
│Service (既有)  │   │(Ludots.Core.Fields.│   │ChunkedNodeGraph  │
│                │   │ Influence)          │   │Store (既有)      │
│                │   │ChunkedField2D<float>│   │                  │
└────────────────┘   └────────────────────┘   └──────────────────┘
```

## 密度参数与范围尺度（统一定义）

两套体系共享同一组尺度语义，避免各说各话：

- **密度 (density)** = `cellSizeCm`：每个采样单元的世界尺寸（厘米）。越小越精细、越贵。
  - Influence Map：field 的 cell 大小。
  - EQS Grid/Donut：候选点间距 = `cellSizeCm`。
- **范围尺度 (range scale)**：
  - Influence Map：`chunkSizeCells`（分块尺寸，2 的幂）+ 投影半径 `radiusCm`。
  - EQS：`extentCm`（生成器覆盖的世界半径/边长）。
- 两者都用 `WorldCmInt2` 定点坐标，保证确定性（无浮点漂移）。

## EQS 组件契约

### Generator（候选生成）
`IEqsGenerator.Generate(WorldCmInt2 origin, Span<EqsItem> buffer) -> int count`

内置生成器：
- `GridGenerator(extentCm, cellSizeCm)` — 方形网格（density=cellSizeCm）
- `RingGenerator(radiusCm, count)` — 环（Unreal Ring）
- `DonutGenerator(innerCm, outerCm, cellSizeCm)` — 圆环带
- `CircleGenerator(radiusCm, cellSizeCm)` — 实心圆盘
- `NodeGenerator(transportNetwork, radiusCm)` — transport 图节点
- `BoardCellGenerator(boardConfig, radiusCells)` — 棋盘格

### Test/Score（打分）
`IEqsTest.Score(in EqsContext ctx, ref EqsItem item)` — 修改 item.Score / item.Filtered

内置测试：
- `DistanceTest(preferNear|preferFar, min, max)`
- `OverlapTest(shape, relationship)` — 复用 `ISpatialQueryService` cast 函数统计范围内实体
- `InfluenceTest(fieldKey, preferLow|preferHigh)` — 采样 influence map
- `PathReachableTest` — transport network 可达性（可选，后续）

### Selection（选择）
- `Best` — 最高分
- `TopN` — 前 N
- `AboveThreshold` — 阈值过滤

## Influence Map 契约

`InfluenceField` 包装 `ChunkedField2D<float>`：
- `Stamp(WorldCmInt2 center, int radiusCm, float peak, FalloffKind)` — 投影一个源（径向衰减）
- `Sample(WorldCmInt2 world) -> float` — 采样
- `Decay(float factor)` — 全场衰减（时间演化）
- `Clear()`

`FalloffKind`：`Constant` / `Linear` / `Quadratic`。

多个命名场（threat / opportunity / ally-density）由 `InfluenceFieldRegistry` 按 key 管理。

## 确定性与性能

- 全部走定点整数坐标（`WorldCmInt2` + cm）。
- warm path 0-alloc：调用方提供 `Span<EqsItem>`，复用 `ChunkedField2D` 的 0-alloc 保证。
- EQS 单次查询上限由 buffer 长度约束（溢出返回 dropped 计数，同 `SpatialQueryResult` 语义）。

## 测试与验收（按 ludots-feature-delivery）

- 最小场景：一个 actor + 若干威胁源，EQS 从环形候选里选"离威胁最远且离目标近"的落点。
- headless E2E + MUD 战斗日志 + 可视化路径（path.mmd）。
- 架构测试：EQS/Influence 不引用 Presentation/Raylib/Skia；EQS 只读 influence，不写。

## 分阶段落地

1. ✅ merge AI/GAS 边界解耦分支
2. Influence 层（`InfluenceField` + `FalloffKind` + registry）+ 单测
3. EQS 层（generators + tests + query runner）+ 单测
4. Utility AI 接入（`InfluenceSample01` / `EqsBestScore01` input kind）
5. 最小场景 headless E2E + 验收产物
