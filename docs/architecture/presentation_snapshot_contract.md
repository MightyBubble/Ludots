# 表现层 visual snapshot contract

本文定义 Core 提供给 adapter 的实体 visual frame snapshot contract。该 contract 以 `PresentationVisualSnapshotBuffer` 为唯一帧快照出口，覆盖 `transform / visibility / identity` 以及 static mesh / skinned mesh 分 lane 所需的运行时字段。Performer 一次性特效、HUD 与调试绘制仍走各自缓冲，不属于本文 contract。

## 1 架构边界

Core 负责每帧生成完整 visual snapshot，不负责 adapter 侧的 persistent manager、dirty diff 或平台对象生命周期。

当前边界由以下实现固定：

* `src/Core/Engine/GameEngine.cs` 创建并每帧清空 `PresentationVisualSnapshotBuffer`，同时保留 `PresentationPrimitiveDrawBuffer` 作为“当前可绘制项”缓冲。
* `src/Core/Presentation/Rendering/PresentationTargetGeneration.cs` 是 adapter 可见的外部 presentation target lifecycle 信号；它只表示 target / projection generation 变化，不表示 retained 内容变化。
* `src/Core/Presentation/Systems/WorldToVisualSyncSystem.cs` 在 render frame 内刷新 `VisualTransform`，即使实体当前处于 `CullState.IsVisible == false`，也不会停止更新 transform。
* `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs` 负责把 `VisualTransform`、`VisualRuntimeState`、`PresentationStableId` 和 `CullState` 组合成 adapter-facing snapshot item。
* `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs` 不输出 dirty-only contract；它始终按 frame snapshot 语义重建当前帧数据。

这意味着 adapter 侧只能在读取 snapshot 后自行维护 persistent static manager、persistent skeleton manager 与 dirty sync。Core 不提供第二套“只发脏项”的并行表现管线，也不允许用 gameplay event 或 content revision 假装 target lifecycle 变化。

## 2 Snapshot 生命周期

`PresentationVisualSnapshotBuffer` 的生命周期与 `GameEngine.Update` 对齐：

1. `src/Core/Engine/GameEngine.cs` 在初始化阶段创建 snapshot buffer。
2. 每帧进入 presentation systems 前，`src/Core/Engine/GameEngine.cs` 先清空 snapshot buffer。
3. `src/Core/Presentation/Systems/WorldToVisualSyncSystem.cs` 刷新 `VisualTransform.Position` 与 `VisualTransform.Rotation`。
4. `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs` 把 renderable visual 写入 snapshot buffer。
5. `src/Core/Presentation/Requests/PresentationRequestFlushSystem.cs` 在 retained content revision 或 `PresentationTargetGeneration.Generation` 变化时，重新把 `StableDrawCache` 投影到 adapter-facing buffers。
6. Adapter 在本帧渲染阶段读取 snapshot buffer，自行完成 persistent object 对齐。

相关回归测试位于：

* `src/Tests/PresentationTests/PresentationFoundationTests.cs`
* `src/Tests/PresentationTests/ProjectionMapPresentationRuntimeTests.cs`
* `src/Tests/ThreeCTests/ThreeCSystemTests.cs`

## 3 Contract 字段与硬约束

| 字段 | 来源 | 硬约束 | 证据 |
|------|------|--------|------|
| `StableId` | `PresentationStableId.Value` | 所有 renderable visual 必须带正整数 `StableId`；缺失或非正值直接抛错，禁止输出 `0` | `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`, `src/Tests/PresentationTests/PresentationFoundationTests.cs` |
| `Position` / `Rotation` / `Scale` | `VisualTransform` + `VisualRuntimeState.BaseScale` | 即使实体 hidden / culled，也必须保持本帧 transform 新鲜，禁止沿用旧帧值 | `src/Core/Presentation/Systems/WorldToVisualSyncSystem.cs`, `src/Tests/ThreeCTests/ThreeCSystemTests.cs` |
| `Visibility` | `VisualRuntimeState.ResolveVisibility(...)` | `Visible`、`Hidden`、`Culled` 三态必须显式输出；adapter 不得再从“是否出现在 draw buffer”反推可见性 | `src/Core/Presentation/Components/VisualRuntimeState.cs`, `src/Core/Presentation/Components/VisualVisibility.cs`, `src/Tests/PresentationTests/PresentationFoundationTests.cs` |
| `RenderPath` / `Animator` | `VisualRuntimeState` + `AnimatorPackedState` | static mesh lane 与 skinned mesh lane 共享同一 snapshot 入口，但保留各自 lane 判定字段 | `src/Core/Presentation/Rendering/PrimitiveDrawItem.cs`, `src/Tests/PresentationTests/ProjectionMapPresentationRuntimeTests.cs` |
| `TemplateId` | `VisualTemplateRef.TemplateId` | entity 带 `VisualTemplateRef` 时必须原样进入 snapshot；无模板实例允许为 `0` | `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`, `src/Tests/PresentationTests/PresentationFoundationTests.cs` |
| `Revision` | `StableDrawCache.ContentRevision` | 只表示 retained visual content 变化；target / viewport / render target lifecycle 不得写入该 revision | `src/Core/Presentation/Rendering/StableDrawCache.cs`, `src/Core/Presentation/Requests/PresentationRequestFlushSystem.cs` |
| `ProjectionGeneration` | `PresentationTargetGeneration.Generation` | adapter target readiness / target replacement 的独立 generation；变化时 retained content 必须重投影，但 `ContentRevision` 保持不变 | `src/Core/Presentation/Rendering/PresentationTargetGeneration.cs`, `src/Tests/PresentationTests/PresentationFoundationTests.cs` |
| Snapshot overflow | `PrimitiveDrawBuffer.TryAdd` 返回值 | snapshot buffer 溢出时直接抛错，禁止静默丢状态 | `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`, `src/Tests/PresentationTests/PresentationFoundationTests.cs` |

## 4 Snapshot buffer 与 draw buffer 的区别

`PresentationVisualSnapshotBuffer` 与 `PresentationPrimitiveDrawBuffer` 语义不同，不能混用：

* `PresentationVisualSnapshotBuffer` 是 adapter-facing frame snapshot，包含 `Visible`、`Hidden`、`Culled` 的 renderable visual，用于 persistent manager 对齐。
* `PresentationPrimitiveDrawBuffer` 是当前帧可直接绘制的可见项集合，仅保留 `Visibility == Visible` 的项。
* `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs` 先写 snapshot，再按 `Visibility == Visible` 过滤写 draw buffer。

因此，adapter 侧如果需要 off-screen 继续移动、reappear 无跳变、despawn / reuse 不串号，必须读 snapshot buffer，不能继续把 draw buffer 当作完整世界状态。

## 5 Adapter 消费要求

对于当前 contract，adapter 侧至少需要满足以下消费方式：

* static manager 以 `StableId` 作为实例键，按 `Visibility` 处理 show / hide / despawn / reuse，不依赖 packet index。
* skeleton manager 以 `StableId` 作为骨架实例键，读取 `Position`、`Rotation` 与 `Animator`，确保 skinned actor 离屏期间继续保持正确状态。
* `RenderPath` 决定实例应进入 static lane 还是 skinned lane，adapter 不得把两条 lane 合并为一个共享实例语义。
* 维护外部 target lifecycle 的 adapter 必须显式持有并更新 Core 的 `PresentationTargetGeneration`。当 target unavailable -> ready 或 target A -> target B 时，adapter 调用 generation service；Core 在下一次 flush 中重投影 retained content。
* persistent / delta 消费方必须比较 `(content/static revision, ProjectionGeneration)`。`Revision` 不变但 `ProjectionGeneration` 变化时，adapter resident state 需要一次 full resync。
* 声明 `PresentationVisualCapabilities.ExternalTargetLifecycle` 的 adapter 必须绑定 `PresentationTargetGeneration`；缺失时 validator 抛出诊断，不允许静默 fallback。

这些要求对应的 follow-up playable 方案见 [../rfcs/RFC-0052-presentation-snapshot-playable-mods.md](../rfcs/RFC-0052-presentation-snapshot-playable-mods.md)。

## 6 相关文档

* adapter 分层：见 [adapter_pattern.md](adapter_pattern.md)
* 表现系统与 Performer：见 [presentation_performer.md](presentation_performer.md)
* RFC playable 设计稿：见 [../rfcs/RFC-0052-presentation-snapshot-playable-mods.md](../rfcs/RFC-0052-presentation-snapshot-playable-mods.md)
