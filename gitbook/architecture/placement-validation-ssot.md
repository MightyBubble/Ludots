# Placement Validation SSOT

Parent: GitHub issue #509. 本页是放置位合法性检测（Placement Validation）的正式架构 SSOT。

## 目标

为瞄准、能力激活、Effect phase graph 提供统一的落点解析与合法性原语，避免 presentation、input、GAS 各自维护平行 clamp / snap 逻辑。

## 复用清单

| 能力 | 归属 |
|---|---|
| 落点解析 | `EffectTargetPointResolver` |
| Phase graph `TargetPos` 注入 | `PlacementPhaseTargetPosResolver` |
| 范围 clamp / 圆内检测 | `PlacementValidation` |
| Entity collection snap | `EntityCollectionStore` + `PlacementValidation.TrySnapToNearestInCollection` |
| NodeGraph 边投影 snap | `GraphEdgeProjectionQuery` + `PlacementValidation.TrySnapToNearestGraphEdge` |
| Graph VM 执行 | `GasGraphOpHandlerTable`, `GraphExecutor.ExecuteValidation` |
| 激活前校验 | `activationPrecondition.validationGraph` |
| OnPropose 拒绝 | `EffectPhaseExecutor.ExecutePhaseWithValidationResult` → `B[0]=0` |

不得新增平行 clamp 工具、平行 target resolver 或平行 graph query helper。

## 分层

```text
Layer 0  PlacementValidation              — Fix64 原语（clamp / circle / collection / graph edge）
Layer 1  EffectTargetPointResolver        — EffectContext + caller params → 世界坐标
         PlacementPhaseTargetPosResolver   — Layer 1 → IntVector2 TargetPos
Layer 2  Graph ops 402–407               — VM 可编排校验 / snap
Layer 3  Phase graph bindings             — OnPropose / OnCalculate / OnApply / validationGraph
```

## Graph Ops（402–407）

| Op | ID | 行为 |
|---|---|---|
| `LoadTargetPosX` | 402 | `I[dst] = TargetPos.X` |
| `LoadTargetPosY` | 403 | `I[dst] = TargetPos.Y` |
| `ClampTargetToRange` | 404 | 以 `E[A]` 为原点、`F[B]` 为射程，clamp `TargetPos`；`B[dst]` = 是否在射程内 |
| `IsPointInCircle` | 405 | `TargetPos` 是否在 `E[A]` 为圆心、`F[B]` 为半径的圆内 |
| `SnapToNearestInCollection` | 406 | 将 `TargetPos` snap 到 `E[A]` 的 collection 最近实体 |
| `SnapToNearestGraphEdge` | 407 | 将 `TargetPos` 投影到 `F[A]` 搜索半径内的最近图边 |

`SnapToNearestGraphEdge` 复用 [Graph Query Services](../reference/graph-query-services.md) 中的 `GraphEdgeProjectionQuery`，需要运行时绑定 `LoadedGraphRuntime`（`GasGraphRuntimeApi.BindLoadedGraphRuntime`）。无图板时返回 `false`，不得 fallback 到原始坐标。

## 管线接线

| 阶段 | `TargetPos` 来源 | 拒绝语义 |
|---|---|---|
| Ability activation | `AbilityExecSystem` 黑板上报点 | `activationPrecondition.validationGraph` → `ExecuteValidation` |
| OnPropose | `PlacementPhaseTargetPosResolver` | phase graph `B[0]=0` → proposal `Cancelled=true` |
| OnCalculate / OnApply | 同上 | 由 graph 自行决定（无全局硬编码） |
| Aim preview | `AbilityAimPresentationRuntime` | presentation 仅预览，不写权威位置 |
| Aim gameplay | `AbilityExecAimSyncSystem` | 写 `AbilityExecInstance.TargetPosCm` + cast blackboard |

Presentation 与 gameplay 共用 Layer 0 `PlacementValidation.ClampToRange`；float 仅在 presentation 边界转换。

## 边界

Placement Validation **不负责**：

- 地形 / navmesh / 障碍扫描（SY37 地形项仍为 backlog）
- Transport 路线评分、lane 选择、业务 route bias
- 结构变更（spawn / destroy entity）

这些属于各自正式管线（Navigation、Transport Network SSOT、GAS spawn queue）。

## 验证

- `src/Tests/GasTests/Effect/PlacementValidationTests.cs`
- `src/Tests/GasTests/Map/TransportNetworkCoreTests.cs`（GraphQuery 基座）
- `InputOrderAbilityAuditTests`、`AbilityAimPresentationRuntimeTests`、`EffectPhaseArchitectureTests`
