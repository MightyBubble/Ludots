# Knowledge 重构方案 — 外部评审记录（claude-opus-4-7）

> 用途：issue #1489 的设计评审原始记录。评审对象是
> `docs/analysis/knowledge-first-principles.md`（我的第一性原理审查）
> 与 issue #1489 的待定项。
> 评审模型：anthropic/claude-opus-4-7，经 `pi` coding CLI 非交互调用。
> **每条关键结论都已用代码核对**（核对结果见 issue #1489 的评论）。

## 评审价值摘要

评审**纠正了我的问题框架**，三点最重要：

1. **集合级产物已经存在**：`DynamicParticipantVisibilityPublisher.Publish` 每帧已经在写
   `EntityCollectionStore.Replace(...)`（带 `(Slot, Revision)`）+ 逐 member knowledge Upsert。
   所以 #1489 不是"要给 knowledge 长一个新集合产物"，而是"**稠密消费方绕开了集合去点查 knowledge**"。
   且写侧已被 `MassNavigationObserverVisibilityBindingSystem` 门控 —— **成本全在读侧**。
2. **按 `MembershipRevision` 门控 AABB 重查是错的**（我撤回该思路是对的，但原因是硬事实）：
   `SpatialPartitionUpdateSystem` 换格走 `partition.Remove/Add` + 直接写 `cellRef.CellX/CellY`，
   **不调 `NotifyComponentChanged<SpatialCellRef>`** ⇒ 10K 每帧成千上万换格而 revision 不动。
   已逐行核对确认。
3. **(`CameraCullingSystem` 不该复用该产物**，理由是语义（相机看得见 vs viewer 知道），不是性能。

以及三条易踩的契约陷阱（tick 参与门控 / 死实体 / compact 不 bump revision）。

---
基于工作树 + `origin/perf/massnav-10k-80fps` 的实际代码核对完毕。先给一个会改变你问题框架的发现，再逐条回答。

---

## 0. 先纠正一个前提：集合级产物**已经存在**，只是 knowledge 没去消费它

`DynamicParticipantVisibilityPublisher`（`src/Core/ParticipantVisibility/DynamicParticipantVisibilityPublisher.cs:59`）每帧干的正是"集合级"这件事：按 `QueryDescription` 取候选 → `FilterCandidates` → `SpatialQueryPostProcessor.SortStableDedup` → **同时**写两处：

- `_collections.Replace(viewer, descriptor, members)`（`:93`）
- 再对每个 member `_knowledge.Upsert(...)`（`UpsertKnowledge`，`:219`）

而 `EntityCollectionStore` 的 `EntityCollectionHandle` 天生带 `(Slot, Revision)`（`src/Core/EntityCollections/EntityCollectionTypes.cs:92`），`EntityCollectionView` 还带 `Signature`。

所以 #1489 不是"knowledge 需要长出一个新集合级产物"，而是"**稠密消费方现在绕开集合，去点查了 knowledge**"。minimap 拿的是 `MinimapMarkerBuffer._owners[i]`（`src/Core/Presentation/Minimap/MinimapRuntime.cs:816`），然后对每个 owner 调一次 `TryResolveDisclosureForViewer`。它手上本来就该有集合。

顺带记一笔：`10K massnav` 的写入侧**已经被门控**了——`MassNavigationObserverVisibilityBindingSystem.Update` 在 `StructuralChangeRevision` / `presenters.Version` / attributeMask 三者不变时直接 return。所以 10,006 次 `Upsert` 不是热路径。**写侧不是成本，读侧才是。** 这决定了方案的重心。

---

## 1. #1489 选哪个形态

**(b) 对，但不要把它当成"新 API"来做——它已经存在；真正要加的是 revision 门控 + 消费方缓存。**

`KnowledgeProjectionStore` 上已经有 caller-buffer 的集合读取：
- `CopyTargets(viewer, currentTick, Span<Entity>)`（`KnowledgeProjectionStore.cs:127`，落到 `EntityKeyedSoaTable.CopySecondaryByPrimary`，`:304`）
- `CopyRecords(...)`（`:137`）

它们zero-alloc、不堆建、走 primary 链。你 issue 里描述的 (b) 已经是现役代码，只是**没人用**（当前唯一非测试调用方是 agent bridge 的 `PresenterTools.cs:102`）。

(a) 按 viewer 维护 `VisibleSetRevision` + 稠密掩码：**掩码可以要，但只能当内部加速结构，不能当公开合同**。原因：掩码按 `Entity.Id` 索引，是全世界宽度；viewer 少、target 多（10K），内存是 `O(entities × viewers)`。真正便宜的形态是 culling 已经在用的 stamp 数组（分支上 `_spatialCandidateStamps`/`_spatialCandidateStamp`，`CameraCullingSystem.cs` 分支版 `:738+`）——`int`/实体/viewer，且不用清理。建议照抄这个手法做"可见性 stamp"，而不是 bitmask。

(b) 若做成公开合同，**要补三样它现在没有的东西**：

**(i) 一个真正会动的 per-viewer revision。** 现在只有 **per-row** revision（`EntityKeyedSoaTable.BumpRevision`，`Upsert`/`DeactivateSlot` 各自 bump）。没有一个"这个 viewer 的行集合变过没有"的聚合计数器。row revision 不能替代——row 数量没变但内容全换了，聚合 revision 照样要动。

**(ii) 过期的陷阱——这是最容易踩的。** `Expire(currentTick)` 只在 `KnowledgeProjectionMaintenance` 的策略到期时才跑，而 `IsActiveAt(slot, currentTick)`（`EntityKeyedSoaTable.cs:~415`）是在**查询时**惰性判过期。也就是说：写侧 revision 没动、但 tick 跨过了某些行的 `ExpiryTick`，缓存集合会**继续吐出已过期行**。所以缓存必须返回 `(revision, tick)` 并在消费方同时比对两者；或者显式约定"本快照只在产出它的那个 tick 有效，除非 store 配成每 tick expiry"。**别只按 revision 门控。**

**(iii) 活性。** `CopySecondaryByPrimary` 直接写 `_secondaryEntities[slot]`，**不过 `World.IsAlive`**。而 `MassNavigationObserverVisibilityBindingSystem` 的 `ExpiryTick = 0`（永不过期）且从不为销毁的 agent `Remove`。所以集合形态会返回**已死实体**。消费方必须自滤，或者 store 在 `RemoveByPrimary` 之外补一条销毁路径。这条要写进合同，不然 minimap 会为死实体投影 marker。

**谁在何时产出：**

- **产出方 = 写入方**（`FogKnowledgeProjector`、publisher、`MassNavigationObserverVisibilityBindingSystem`），在 `Upsert`/`Remove`/`ClearViewer`/`RunMaintenance` 里 bump per-viewer revision。不要做成"resolver 懒重算"——resolver 是无状态的、不知道 tick 边界，懒重算会把成本从中挪回来。
- **消费方按 revision 懒重算**：`CopyVisibleTargetsByRevision(viewer, tick, ref cachedRevision, Span<Entity> buf, out int count)`。caller buffer，不堆建。consumer 自己缓存 buffer + `(revision, tick)`。

**怎么不破坏 L8：** 用**类型**隔开，不用纪律隔开。
- L8 授权路径：`KnowledgeCommandTargetGate`（`src/Core/Input/Interaction/KnowledgeCommandTargetGate.cs`）→ resolver 全宽投影。它现在把 resolver + clock 做成硬构造依赖、且注释明写"missing resolver is a startup failure, never an allow-all fallback"——**保持这样，别给它加集合级重载**。
- 集合级产物：只注册在**新的** service key 上，不挂在 `KnowledgeProjectionResolver` 上。
- 核心不变式：集合级产物**只能用来跳过工作，永远不能用来授予**。度量可见性聚合时宁可**多报**（over-inclusive），因为多报只会掉帧，不会让 fog 下单位变成可命中。这条要写进 ADR 正文，它是这个设计的全部安全性来源。

**(c) 把 presence/position 变成 ECS 组件**：是正确终点，但是**错的起手式**。它直接撞 ADR #191 的 `(viewer, target)` 稀疏对 SSOT 表述。而且见 §6 第 6 条：终点不该是"knowledge 变组件"，而是"**稠密读走集合，稀疏记录留在 knowledge**"——那样 #191 和 #1489 是互补而不是推翻。

---

## 2. aspect 宽度分区（具体类型切分）

现在 `KnowledgeDisclosureRecord` / `KnowledgeProjection` 各带 3×`KnowledgeIdMask256`（96 字节），而 `ProjectionAccumulator.Add`（`KnowledgeProjectionResolver.cs:~390`）**每加一条就 union 三次全部 mask**——这就是你点查成本的主要构成。

建议：

- **新增窄记录** `KnowledgeVisibilityRecord`（`readonly struct`）：`Presence` / `Position` / `Source` / `ObservedTick` / `ExpiryTick`。**不要** `Revision`、不要 `ConfidencePermille`、不要 mask。
  `7fe343843b` 的 `TryResolveDisclosure` 现在是"返回整条 disclosure"，把它收敛成这个类型，别让窄查询的返回类型等于全宽记录。
- **不新造 mask 类型**。`KnowledgeIdMask256` 保持唯一。切分是"窄视图 / 全宽视图"，不是"新 mask 类"。
- **`KnowledgeProjection` 只允许被 resolver 与 INT-8 场景使用**。加一条 ArchitectureTest（`src/Tests/ArchitectureTests/` 已有先例）断言 `MinimapPresentationSystem` / `MinimapRuntime` / `CameraCullingSystem` / `PresenterEmitSystem` 所在程序集**不得引用** `KnowledgeProjection` / `KnowledgeProjectionStore.CopyRecords`。否则 INT-8 一旦落地，mask 会顺着 `TryResolveDisclosure` 长回稠密路径，你又回到原地——这正是 `docs/analysis/knowledge-first-principles.md` §2.3 担心的事，用测试钉死它比用注释钉死它可靠。
- store 的 SoA payload 这轮**不动**。收窄 payload 是 ADR 级改动（动 `KnowledgeIdMask256` 的存储布局 + 序列化），单独一单。

---

## 3. `CameraCullingSystem` 该不该复用同一产物

**不该。让它继续对 knowledge 一无所知。** 现在 `CameraCullingSystem.cs` 里零 knowledge 引用，这是对的，别动。

理由不是性能，是语义：剔除回答的是"**相机**看得见什么"，knowledge 回答的是"**viewer 知道**什么"。fog 内单位的 last-known 位置仍要在 minimap / HUD 上画——一旦把它并进剔除，你就会在同一帧里同时需要"剔除它"和"保留它"，然后开始打补丁。RFC-0065 DEC-14 / INT-4 管的是 pointer 命中，不是渲染剔除。这 2.7–4.7ms 不是 knowledge 问题。

它的真实成本拆解（都跟 knowledge 无关）：

1. **`SpatialQueryPostProcessor.SortStableDedup`**（`src/Core/Spatial/SpatialQueryService.cs:96-100`）。它对整个 AABB 结果做 `span.Sort(StableComparerInstance)`——无堆分配，但是 O(n log n) **带接口调用**的比较器，在 10K+ 候选上是 `cullSpatial` 的实打实一块。而且 `GridSpatialPartitionWorld.Query` 是逐 cell 拼接（`:97-129`），一个实体只在一个 cell ⇒ **同一 cell 矩形内不可能重复**。dedup 是为可能重叠的 backend 存在的。按 backend 验证后 gate 掉它，是一个不需要改 Core 接口的收益。
2. **`_spatialCandidates.Contains(entity)`**（`:1907`）——`HashSet<Entity>` 每动态实体一次查找。分支上已换成 stamp 数组，**这个改法对，让它落**（对比一下：`HashSet` 的 `GetHashCode` 还要拼 3 个 int）。
3. **`HasDynamicCullWork()`**（`:690`）：**每个 pass 跑 10 次 `World.Query` 判空**，两个 pass 就是 20 次 archetype 遍历/帧。用一个 structure-version 计数器缓存这个 bool，是白送的钱。

**关于 revision 门控 AABB 重查——你撤回是对的，而且原因是硬事实：**

- `SpatialQueryService.MembershipRevision`（分支 `SpatialQueryService.cs:25`）只在 `MarkBoundsChanged`（`:44-49`）里对 `SpatialCellRef | SuspendedTag | PresentationStaticTransform | SpatialPartitionExcluded | PresentationDestroyPending` 5 个类型自增，外加 `SetBackend`（`:123`）。
- 但 `SpatialPartitionUpdateSystem` 换格走的是 **ref 直接改 `cellRef` + `partition.Remove/Add`**（`:517-519`），**不调 `World.NotifyComponentChanged<SpatialCellRef>`**；`MoveJob.Update`（`:419-426`）这条 inline 热路径同样不调。全文件里唯一一次 `NotifyComponentChanged<SpatialCellRef>` 在 `Deactivate`（`:186`）。
- 所以：**10K 场景下每帧有成千上万个实体真实换格，而 `MembershipRevision` 一动不动。** 按它门控 = 直接吃陈旧候选集，且恰好在这个 showcase 上最严重。

要修也只有一条路：把 revision 下沉到 `ISpatialPartitionWorld`（`src/Core/Spatial/ISpatialPartitionWorld.cs:12-19` 只有 `Add/Remove/Query/Clear`），在 `Add`/`Remove`/`Clear` 里自增，`SpatialQueryService.MembershipRevision` 透传。这是 **Core 接口改动，§4.1 基建任务**。而且即便做了，收益窗口是"相机静止且无人跨格"——10K massnav 相机通常在动，**收益接近零**。

**结论：别做。** 把这个事实写进 #1489 的正文，防止下一个人再试一遍（分支上 `GraphAimSourceRuntime.cs:76` 已经在用 `MembershipRevision` 做 source 校验，它只关心"没换格"这个方向，语义上安全；但那是校验不是缓存，别类推）。

---

## 4. `PresenterEmitSystem` 降本

先确认你真的付了这个钱：`EmitQuery`（`PresenterEmitSystem.cs:24-27`）是 `WithAll<..., PerfHasEmitWork>().WithNone<PerfStaticStableVisual>()`。命中后的快路径 `ProcessSingleVisualProxyFastChunkEntity`（`:1103`）**无条件**调 `EmitVisualProxyFast`（`:1081`）+ `UpdateEmitCache`（`:1091`）——**没有任何 dirty 判断**。

对比 `ProcessDirtyStaticStableEmit`（`:1220` 附近），它有完整四条 clean 判断（`versionClean && positionClean && ownerCullClean && lodClean`）。**快路径缺的正是这个对称。** 这是第一顺位。

但有一个前置事实必须先核实，否则这个 early-out 会直接把实体从画面里删掉：**draw/request buffer 是不是每帧 clear+rebuild？** `gitbook/architecture/presenter-compiled-lanes.md` §1.5 把"Draw buffer 持久化，不每帧 clear+rebuild"列为**目标**而非已落地。先查 `_requests` / `StableDrawCache` 的实际生命周期，再决定 early-out 能不能直接上。持久化了 → 免费；没持久化 → 先做持久化，那本来就是既定 lane 设计。

按价值排序：

**a. 快路径 emit-cache early-out**（上段）。验收：`PresenterEmit` 2–5ms → ≤2ms，固定相机路径的帧 hash 与基线逐帧一致。

**b. 干掉 `PerfHasEmitWork` 的标记抖动。** `SyncTickBehaviorMarker<T>`（`PresenterEntityRuntime.cs:1803`）是 `Has` → `Add/Remove`，即 archetype 迁移。`SyncEmitWorkMarkers`（`:1572`）的调用点包括 `SetBehaviorActive`（`:1668`）和**每一条 cull 可见性同步路径**（`:4406`、`:4685`、`:2380`、`:3231`）。10K 动态 presenter 的 cull 每帧都在动 → 每帧都在探 `Has`，并在可见性翻转时迁移 archetype。
做两件事：把 `hasEmitWork` 的结果**缓存进 `PresenterEmitCache`**（那个 struct 已经是 byte 密集的，`PresenterEmitCache.cs:7-17`，有位置），这样快路径的 `Has<>` 探测消失；并且只在 `activeBehaviorMask` 真变时才重算——`hasEmitWork` 对给定 (definition, mask) 是常量。

**c. 把定义类别下沉成结构标记，拆 query。** `ResolveCachedDefinition`（`:180`）只能按 `DefId` 连续时命中；Archetype 按组件签名分组、不按 `DefId`，所以 chunk 内会抖动，`IsVisualProxyFastDefinition`（`:1174`）会被反复求值。
加一个 `PerfVisualProxyFast` 结构标记（创建/behaviour 变更时同步，和现有 marker 机制一致），把 `EmitQuery` 拆成"快代理"和"一般"两条 query，内层循环变均匀。这正好是 `presenter-compiled-lanes.md` §8.4 自己列的未清债（"上帝类继续拆薄"），**不是新方向，是既定工作**，所以它属于 §4.1 要出方案但方向已批准的那类。

**不碰 HUD / mark 数量**——你的约束成立，30K 场景 GPU 85%+ 是几何地板，这里的 2–5ms 只在 10K 场景（CPU 侧 80 FPS 预算 12.5ms）才计入临界路径。所以 a/b/c 的优先级跟 knowledge 那几项**同级**，不是次级。

---

## 5. （按要求不展开）同意结论：瓶颈在几何/片元，HUD 条目数与 mark 数量不是杠杆。

---

## 6. 实施顺序 + 验收指标 + §4.1 归类

**P0 — 可立刻做（但第 1 项按 §4.1 仍要先把方案写进 #1489 正文，因为是给 Core 类加接口）**

1. `KnowledgeProjectionStore` 加 per-viewer `VisibilityRevision`（在 `Upsert`/`Remove`/`ClearViewer`/`RunMaintenance` 自增）+ `CopyVisibleTargetsByRevision(viewer, tick, ref revision, Span<Entity>, out int)`。消费方 `MinimapRuntime.ProjectMarkers`（`:816-833`）改为先取集合、再逐实体判 stamp。
   - **验收**：`minimapProject` 1.92ms → ≤1.0ms（你的天花板实验说 0.8ms 可达）；10000 markers 投影数不变。
   - **必带**：契约里写清 (ii) tick 参与门控、(iii) 返回集可能含死实体、消费方自滤。
2. 收窄 `TryResolveDisclosure` 返回类型为 `KnowledgeVisibilityRecord`；加 ArchitectureTest 钉死稠密消费方不得引用全宽投影。
   - **验收**：knowledge 在 minimap 中的占比 ≤0.5ms；测试在有人加宽时变红。
3. `PresenterEmitSystem`：先核实 draw buffer 持久性，再上快路径 early-out。
   - **验收**：`PresenterEmit` ≤2ms；固定相机路径帧 hash 逐帧一致。
4. `CameraCullingSystem`：land stamp 数组；按 backend 验证后 gate `SortStableDedup`；缓存 `HasDynamicCullWork()`。
   - **验收**：`cullSpatial` ≤1.0ms，`cullDyn` ≤1.2ms。

**P1 — §4.1「先出方案」的基建任务，先写 ADR / 提案，别先写码**

5. `ISpatialPartitionWorld.MembershipRevision` —— **建议不做**，把 §3 的事实写进 #1489 存档并关闭该分支思路。
6. **终点态：稠密读走集合，knowledge 只留稀疏/授权。** 这不是"把 presence/position 变 ECS 组件"（那会推翻 #191），而是承认 `EntityCollectionStore` 已经是带 revision + signature + role 的集合级基座，让 minimap / HUD /（未来的）任何稠密方读它，只让 L8 走 resolver。这需要新 ADR，且必须显式处理它跟 #191 "Knowledge Projection 是唯一的有限信息读取路径"的关系——我倾向的措辞是：**knowledge 是唯一的信息*语义*真源，集合是它的稠密*物化视图*，LLM 式地"绕过 knowledge 读 sim 真值"依然禁止**。这条 ADR 是 #1489 真正的收口。
7. PresenterEmit lane 拆分（上 §4c）——按 `gitbook/architecture/presenter-compiled-lanes.md` §8.4 的既定债推进。

**另外三个该单独立单、不要塞进 #1489 的发现：**

- `DynamicParticipantVisibilityPublisher.Publish`（`:59`）**每帧**对每个 binding 做 `CountEntities` + `GetEntities` + 全量 `FilterCandidates` + `SortStableDedup` + `MatchesPrevious`。binding 数少时无所谓，宽 query 上是又一次稠密每帧扫描。
- `EntityKeyedSoaTable.Compact()`（`:340`）**不 bump 任何 revision**。它移动 slot、`RebuildPrimaryIndex` 重建链。row revision 被 `CopySlot` 带过去，所以行级语义没问题；但如果你把 per-viewer revision 建在"primary 链变了就 bump"上，会在 compact 时静默漏失效。用显式计数器，别用结构推断。
- `MassNavigationObserverVisibilityBindingSystem` 用 `ExpiryTick = 0`（永不过期）且不清理销毁 agent 的 knowledge 行。当前无害（消费方按 marker owner 反查，永不查到死实体），但一旦转向 §6 的集合级形态就会变成可见 bug。
