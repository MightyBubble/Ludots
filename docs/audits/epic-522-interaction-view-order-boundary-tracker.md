# Epic #522 — Issue Tracker（仓库镜像）

GitHub Epic: https://github.com/MightyBubble/Ludots/issues/522  
RFC: [`RFC-0061-interaction-view-order-massnav-boundary-unification.md`](../rfcs/RFC-0061-interaction-view-order-massnav-boundary-unification.md)

Status: **Complete** (branch `cursor/ord-1-massnav-input-removal-18f0`, PR #535)

## Sub-issues

| Phase | ID | GitHub | Title | Status |
|-------|-----|--------|-------|--------|
| A | ORD-1 | [#523](https://github.com/MightyBubble/Ludots/issues/523) | Remove MassNavigationLocalCommandInputSystem | Done |
| A | ORD-2 | [#524](https://github.com/MightyBubble/Ludots/issues/524) | Remove MassNavigationSelectionSyncSystem | Done |
| A | ORD-3 | [#525](https://github.com/MightyBubble/Ludots/issues/525) | Relocate selection bind/clear out of MassNav | Done |
| A | ORD-4 | [#526](https://github.com/MightyBubble/Ludots/issues/526) | Extract SubmitMoveCommand to Order intake | Done |
| B | ORD-5 | [#527](https://github.com/MightyBubble/Ludots/issues/527) | Unified OrderQueue intake | Done |
| B | ORD-6 | [#528](https://github.com/MightyBubble/Ludots/issues/528) | Command semantic intent (#503) | Done |
| C | ORD-7 | [#529](https://github.com/MightyBubble/Ludots/issues/529) | EntityViewProfile SSOT | Done |
| C | ORD-8 | [#530](https://github.com/MightyBubble/Ludots/issues/530) | Migrate consumers to EntityView | Done |
| C | ORD-9 | [#531](https://github.com/MightyBubble/Ludots/issues/531) | Retire Selection dual-write | Done |
| D | ORD-10 | [#532](https://github.com/MightyBubble/Ludots/issues/532) | Update formal docs | Done |
| D | ORD-11 | [#533](https://github.com/MightyBubble/Ludots/issues/533) | Regression + test cleanup | Done |

## Acceptance (Epic #522)

- [x] Formation capability showcase playable：框选 + 右键 move 走 OrderQueue → ingestion → movement
- [x] MassNav Core 无 Input/Selection import（ingestion + nav components only；Input 系统由 CoreInputMod 注册）
- [x] 正式文档与 Epic issue 子项全部关闭（仓库镜像；**GitHub issue 本 PR 不自动关闭**，merge 后人工 triage 遗留尾巴再关）

## Related

- #503, #519
- 误创建测试 issue #521（可手动关闭）

## Post-Epic 遗留尾巴（不在 #522 范围内，issue 暂不关闭）

Epic 验收项已满足；以下项建议在 merge 后单独开 follow-up issue 跟踪，**本 PR 不关闭 #522 / #523–#533**。

### P1 — Legacy 消费者仍读 Selection 视图

| 位置 | 现状 | 建议 |
|------|------|------|
| `mods/CoreInputMod/Systems/SelectedMovePathPresentationSystem.cs` | 读 `SelectionViewRuntime` + `SelectionRuntime` | 改读 EntityView display/command collection 或 move-path 专用 collection |
| `src/Core/Presentation/Minimap/MinimapRuntime.cs` | `SelectionContextRuntime.TryGetCurrentPrimary` | 改读 EntityView command source |
| `src/Adapters/Raylib/.../RaylibHostLoop.cs`（若存在） | 同上模式 | 同上 |
| `SelectionContextRuntime` / `SelectionViewRuntime` | `GetCurrentCount` / `SnapshotCurrentSelection` 仍解析 Selection 容器 | gameplay 路径应优先 EntityView；API 可 deprecate 或加 EntityView 分支 |
| 多个 showcase mod（interaction、road_network、spatial_bounds、RTS production 等） | 运行时仍 `TryBindView` + 读 `SelectionContextRuntime` | 显式 presentation 绑定可保留，但应文档化；命令路径勿再依赖 |

### P2 — 双轨 presentation 事件

| 位置 | 现状 | 建议 |
|------|------|------|
| `SelectionPresentationEventSystem` | 仍监听 `SelectionRuntime` 容器 diff | acquisition 已走 `EntityViewDisplaySelectionPresentationEventSystem`；评估是否仅保留 mod 显式写入路径，或统一为 EntityCollection 事件 |
| `EntityViewDisplaySelectionPresentationEventSystem` | 仅桥接 **当前 EntityView display collection** → `SelectionMemberAdded` | 多 viewKey（Formation / CommandPreview）需扩展 profile 映射或改用 `EntityCollectionMemberAdded` performer 规则 |

### P3 — API / telemetry 僵尸

| 位置 | 现状 | 建议 |
|------|------|------|
| `OrderBufferSystem.SubmitOrder` | `internal`，生产零 caller | 删除或加 analyzer/contract test 禁止复活 |
| `MassNavigationTelemetry.SelectionSyncMs` / `ObserveSelectionSync*` | 命名仍指已删的 selection sync | 重命名为 command/ingestion 语义或移除 |
| `MassNavigationSimulationRuntime.cs` | 可能残留 unused `using Ludots.Core.Input.Selection` | 清理 |
| `SelectionContextRuntime.TrySetCurrentView` | 仍 `TryBindView` 到 Selection（EntityView globals 已分离） | mod 显式绑定可保留；考虑拆成 `TrySetEntityView` + 可选 `TryBindSelectionView` |

### P4 — 文档 / 生成页

| 位置 | 现状 | 建议 |
|------|------|------|
| `docs/prd/08-input-orders.html` | 可能仍引用已删 MassNav input/selection sync | 随 PRD 再生成或手工 patch |
| `docs/reference/glossary.html` | 同上 | 同上 |
| `entity_selection_architecture.md` | 正文仍部分以 SelectionRuntime 为 SSOT（顶部已有 deprecation） | 分阶段改写「视图选择」章节 |

### P5 — orthogonal open issues

- **#519** — 移动线 overlay 污染 owner-payload performer 同步（Epic 未阻塞，Formation 测试已知）
- **#503** — Command semantic 已在 ORD-6 落地；若 issue 仍 open，可关 note 并链到 `CommandInteractionSemanticSystem`

### 测试债务

- `src/Tests/GasTests/Production/*`、`ThreeCTests/*` 等大量断言仍基于 `SelectionContextRuntime` / `TryBindView` setup
- 与 Formation 合约测试不同，这些尚未迁移到 EntityView command source 断言
