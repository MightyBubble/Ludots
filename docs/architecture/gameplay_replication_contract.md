# gameplay replication contract

本文定义 Ludots 当前用于 authoritative gameplay replication 的最小联机合同。该合同服务于 DS + 状态同步基础层，明确把 gameplay truth 与 adapter-facing visual snapshot 分离，避免把 `PresentationVisualSnapshotBuffer` 误当成联机状态源。

当前实现入口与证据路径：

* `src/Core/Networking/GameplayReplicationSnapshotBuffer.cs`
* `src/Core/Networking/Systems/GameplayReplicationBootstrapSystem.cs`
* `src/Core/Networking/Systems/GameplayReplicationEmitSystem.cs`
* `src/Core/Engine/GameEngine.cs`
* `src/Adapters/Web/Ludots.Adapter.Web/Streaming/GameplayReplicationSnapshotExtractor.cs`
* `src/Apps/Web/Ludots.App.Web/Program.cs`
* `src/Tests/NetworkingTests/GameplayReplicationFoundationTests.cs`

## 1 架构边界

当前联机基础层只定义 authoritative gameplay snapshot，不实现 transport、回滚、lockstep 输入聚合或平台房间管理。

边界约束如下：

* gameplay truth 只来自 fixed-step 后的 ECS 逻辑态，不来自 render frame 的表现层缓冲。
* `PresentationVisualSnapshotBuffer` 继续只服务 adapter 视觉同步，详见 [presentation_snapshot_contract.md](presentation_snapshot_contract.md)。
* gameplay replication 当前服务于两类需求：
  * DS 场景下对 authoritative world state 的调试导出与外部观测。
  * 状态同步方案下的最小实体位姿 / 阵营 / owner / 朝向复制基础。
* 帧同步方案当前未落地；未来如实现 lockstep，只能复用本合同作为 debug / observer 视图，不能反向把它当输入真相。

## 2 Snapshot 生命周期

authoritative gameplay snapshot 的生命周期与 fixed-step 对齐，而不是与 render frame 对齐：

1. `src/Core/Engine/GameEngine.cs` 在初始化阶段创建 `GameplayReplicationEntityIdAllocator` 与 `GameplayReplicationSnapshotBuffer`，并通过 `CoreServiceKeys` 暴露服务。
2. `src/Core/Engine/GameEngine.cs` 把 `GameplayReplicationBootstrapSystem` 和 `GameplayReplicationEmitSystem` 注册到 `SystemGroup.EventDispatch`，位于 `GameplayEventDispatchSystem` 之后、`GasBudgetReportSystem` 之前。
3. `src/Core/Networking/Systems/GameplayReplicationBootstrapSystem.cs` 为所有带 `WorldPositionCm` 且缺少 replication id 的实体补齐稳定 `GameplayReplicationEntityId`。
4. `src/Core/Networking/Systems/GameplayReplicationEmitSystem.cs` 在同一 fixed-step 内重建最新 authoritative snapshot，并确保当步新增实体不会被遗漏。
5. `src/Core/Engine/GameEngine.cs` 的 render `Update(float dt)` 不会清理 `GameplayReplicationSnapshotBuffer`，因此最新快照会保留到下一次 fixed-step rebuild。

这意味着：

* 同一帧没有新的逻辑步时，外部 observer 仍可读取上一逻辑步的 authoritative snapshot。
* 联机层如需 diff、压缩或按连接裁剪，必须在此合同之外的 adapter / transport 层完成，Core 不提供第二套“只发脏数据”的并行运行时。

## 3 Contract 字段与硬约束

当前 snapshot item 由 `src/Core/Networking/GameplayReplicationSnapshotItem.cs` 定义，字段语义如下：

| 字段 | 来源 | 约束 |
|------|------|------|
| `ReplicationEntityId` | `GameplayReplicationEntityId.Value` | 必须为正整数；缺失时由 bootstrap / emit 在 authoritative fixed-step 内补齐 |
| `PositionXRaw` / `PositionYRaw` | `WorldPositionCm.Value` | 使用 `Fix64.RawValue` 输出，保持状态同步可复用的精确性 |
| `FacingAngleRad` | `FacingDirection.AngleRad` | 仅在 `Flags.HasFacing` 置位时有效 |
| `TeamId` | `Team.Id` | 仅在 `Flags.HasTeam` 置位时有效 |
| `PlayerId` | `PlayerOwner.PlayerId` | 仅在 `Flags.HasPlayerOwner` 置位时有效 |
| `PresentationStableId` | `PresentationStableId.Value` | 仅作为 cross-reference；不能反向定义 gameplay identity |
| `Flags` | `GameplayReplicationSnapshotFlags` | 显式声明可选字段是否存在，禁止 consumer 通过默认值猜测 |

补充约束：

* snapshot buffer 溢出只累计 `DroppedSinceClear` / `DroppedTotal`，不静默扩容。
* contract 只复制 gameplay 所需字段，不包含 mesh、材质、缩放、动画控制器等 render-only 数据。
* `RuntimeEntitySpawnQueue -> RuntimeEntitySpawnSystem` 仍是运行时结构变化唯一入口，详见 [runtime_entity_spawn_flow.md](runtime_entity_spawn_flow.md)。

## 4 Web 调试读取面

当前 Web 适配层提供只读调试出口：

* `src/Adapters/Web/Ludots.Adapter.Web/Streaming/GameplayReplicationSnapshotExtractor.cs`
* `src/Apps/Web/Ludots.App.Web/Program.cs`

接口行为：

* `GET /api/runtime/gameplay-snapshot`
* 返回最新 authoritative gameplay snapshot 的 JSON 视图。
* 该端点是 debug / observability 入口，不承诺客户端复制协议，也不承诺 backward compatibility。

因此：

* transport 层后续若引入二进制状态同步协议，应复用同一 Core snapshot 作为数据源。
* Web 端点只承担“把当前 authoritative snapshot 可视化”的职责，不负责连接态、interest management 或补包策略。

## 5 当前适配的联机方案位置

本合同对应三类联机方案中的基础层位置如下：

* 状态同步：直接复用本 snapshot 作为 authoritative state source，再在 adapter / gateway 侧做序列化、裁剪、压缩和纠错。
* DS 方案：直接复用本 snapshot 作为服务器真相导出、回放抽样和观战调试入口。
* 帧同步：当前不直接消费本 snapshot 做判定；它只适合作为 lockstep 运行时的旁路观测合同，用于验收、录制和 debug。

## 6 验证证据

自动化证据位于：

* `src/Tests/NetworkingTests/GameplayReplicationFoundationTests.cs`
* `artifacts/acceptance/gameplay-replication-foundation/trace.jsonl`
* `artifacts/acceptance/gameplay-replication-foundation/battle-report.md`
* `artifacts/acceptance/gameplay-replication-foundation/path.mmd`

其中：

* 系统顺序守卫验证 `EventDispatch` 中 authoritative snapshot 的注册顺序。
* integration 验证覆盖 runtime spawn、team / owner 复制、稳定 replication id 与 Web extractor 输出。
* acceptance artifact 记录 happy path 与 guard branch：owned spawn 复制 owner 信息，neutral spawn 则显式缺失可选 ownership flags。

## 7 相关文档

* 运行时实体生成链路：见 [runtime_entity_spawn_flow.md](runtime_entity_spawn_flow.md)
* 表现层 visual snapshot 合同：见 [presentation_snapshot_contract.md](presentation_snapshot_contract.md)
* 启动与宿主入口：见 [startup_entrypoints.md](startup_entrypoints.md)
