# RFC: EQS + Influence Map 空间推理体系

## 状态
草案 — 基础设施已落地；主循环投影与 config authoring 未接线（branch `codex/merge-ai-boundary-eqs`）

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
| 网格候选 | `FieldGridSpec2D` | `src/Core/Fields/` |
| Utility 打分接入 | `UtilityAiInputKind` 扩展点 | `src/Core/Gameplay/AI/Utility/` |
| 确定性数学 | `WorldCmInt2`、`MathUtil.FloorDiv` | `src/Core/Math/` |

## 分层架构

```
┌─────────────────────────────────────────────────────────┐
│  Utility AI (Deliberation)                                │
│  UtilityAiInputKind.InfluenceSample01（可选注入 hook）     │  ← 已实现枚举 + 运行时采样
│  EqsBestScore01（Future）                                  │
└───────────────────────┬───────────────────────────────────┘
                        │ 只读采样（缺依赖硬失败，禁止静默 0）
┌───────────────────────▼───────────────────────────────────┐
│  EQS 层 (Ludots.Core.Spatial.Eqs)                          │
│  ┌──────────────┐   ┌───────────────┐   ┌──────────────┐  │
│  │ Generator    │──▶│ Test/Score    │──▶│ Selection    │  │
│  │ Grid/Ring/   │   │ Distance/     │   │ Best/TopN/   │  │
│  │ Donut/Node/  │   │ Overlap(cast)/│   │ Threshold    │  │
│  │ Circle       │   │ Influence     │   │              │  │
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
- 坐标契约用 `WorldCmInt2` 定点。`Stamp` 径向衰减当前仍使用 `Math.Sqrt` / float（边界：非全定点；定点近似为后续工作）。

## EQS 组件契约

### Generator（候选生成）
`IEqsGenerator.Generate(WorldCmInt2 origin, Span<EqsItem> buffer) -> int count`

已实现：
- `GridGenerator(extentCm, cellSizeCm)` — 方形网格（density=cellSizeCm）
- `RingGenerator(radiusCm, count)` — 环
- `DonutGenerator(innerCm, outerCm, cellSizeCm)` — 圆环带
- `CircleGenerator(radiusCm, cellSizeCm)` — 实心圆盘
- `NodeGenerator(transportNetwork, radiusCm)` — transport 图节点

Future：
- `BoardCellGenerator(boardConfig, radiusCells)` — 棋盘格

### Test/Score（打分）
`IEqsTest.Score(in EqsContext ctx, ref EqsItem item)` — 修改 item.Score / item.Filtered

已实现：
- `DistanceTest(preferNear|preferFar, min, max)`
- `OverlapTest(shape)` — 复用 `ISpatialQueryService`；缺服务硬失败
- `InfluenceTest(fieldKey, preferLow|preferHigh)` — 采样 influence；缺场/缺 registry 硬失败

Future：
- `PathReachableTest` — transport network 可达性

### Selection（选择）
- `Best` — 最高分
- `TopN` — 前 N
- `AboveThreshold` — 阈值过滤

## Influence Map 契约

`InfluenceField` 包装 `ChunkedField2D<float>`：
- `Stamp(WorldCmInt2 center, int radiusCm, float peak, FalloffKind)` — 投影一个源（径向衰减）
- `Sample(WorldCmInt2 world) -> float` — 采样
- `Decay(float factor)` — 全场衰减：`ChunkedField2D.ScaleNonDefault` 就地乘 SoA float 通道（0-alloc，禁止分页重扫）
- `Clear()`

`FalloffKind`：`Constant` / `Linear` / `Quadratic`；未知枚举硬失败。

多个命名场（threat / opportunity / ally-density）由 `InfluenceFieldRegistry` 按 key 管理。

## 确定性与性能

- 坐标：`WorldCmInt2` + cm。
- warm path 0-alloc：调用方提供 `Span<EqsItem>`；Decay/Scale 走 SoA float channel，禁止热路径堆分配。
- EQS 单次查询上限由 buffer 长度约束（溢出返回 dropped 计数，同 `SpatialQueryResult` 语义）。
- **NO FALLBACK**：Influence / Overlap / Utility `InfluenceSample01` 缺依赖一律硬失败，禁止静默当 0。

## 未接线清单（诚实完成度）

- EQS 仍代码拼装，无 config loader / authoring
- Influence 无主循环投影 System（registry 可选注入，非 gameplay 全链路已通）
- `UtilityAiInputKind.InfluenceSample01`：运行时 hook 已实现；`AI/inputs.json` 暂拒载（待投影接线）
- `EqsBestScore01`、`BoardCellGenerator`：Future
- `Stamp` 定点近似：Future

## 测试与验收（按 ludots-feature-delivery）

- 最小场景：一个 actor + 若干威胁源，EQS 从环形候选里选"离威胁最远且离目标近"的落点。
- headless E2E + MUD 战斗日志 + 可视化路径（path.mmd）。
- 架构测试：EQS/Influence 不引用 Presentation/Raylib/Skia；EQS 只读 influence，不写。
- Decay：>256 非默认格全场正确衰减 + warm path 0-alloc。

## 分阶段落地

1. ✅ merge AI/GAS 边界解耦分支
2. ✅ Influence 层（`InfluenceField` + `FalloffKind` + registry）+ 单测
3. ✅ EQS 层（generators + tests + query runner）+ 单测
4. ✅ Utility AI 可选 hook：`InfluenceSample01`（硬失败缺依赖；config authoring Future）
5. ✅ 最小场景 headless E2E + 验收产物
6. ⬜ EQS config authoring
7. ⬜ Influence 主循环投影 System
8. ⬜ `EqsBestScore01` / `BoardCellGenerator` / 定点 Stamp（若仍需要）
