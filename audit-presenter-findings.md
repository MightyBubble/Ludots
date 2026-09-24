# 91618843a4：Presenter、HUD 与整帧分配审计证据

审查源码为 `C:\001_AI\LudotsAudit10k` 的 `91618843a4be547611363e7971282fcc489ca9ee`，比较 `origin/main = be8f0f69d2947613559f717d6e993631fb2533f4`。实验只在 `C:\001_AI\_audit_1485_ab` 进行。没有修改生产工作区、提交代码或修改 `EffectPhaseSideEffectTransaction`。本文件是完整审计报告的证据附件。

## 1. HUD 地形遮挡重复执行 20,000 次射线检测

同版本局部 A/B 已重新落盘：10K 单位、20K HUD 条目，每臂先暖机 100 次投影，再采样 31 次；开启地形遮挡的投影中位数 10.8315 ms、p95 12.6990 ms，关闭时中位数 1.0823 ms、p95 1.7783 ms。两臂 world/screen 均为 20,000 条目，31 次采样总分配均为 0 B，Gen0/Gen1 均为 0。数据来自 [.audit-hud-occlusion-ab.log](C:/001_AI/_audit_1485_ab/.audit-hud-occlusion-ab.log) 的 196、388 行；这是静止世界中强制更新投影的 headless 局部测试，不是整机 FPS。根审计另有同版本整机 A/B，其统计以根审计原始数据为准。

调用链：`RaylibHostLoop` → `WorldHudToScreenSystem.Update` → `TryGetOwnerFrameProjection` → `IsTerrainVisible` → `IContinuousHeightmap.TryRaycastGround`。对应源码：

- `src/Core/Presentation/Systems/WorldHudToScreenSystem.cs:142` 遍历 HUD；`:164` 查帧内投影缓存；`:174` 调遮挡；`:478` 调射线检测。
- 同文件 `:391–393` 仅合并相邻且 owner 与 worldPosition 都相等的条目；`:507–509` 缓存键同样要求位置相等。血条与数字位置不同，因此同一 owner 的两个 HUD 不共享一次遮挡判定。
- `:427–428` 每 owner 每帧一次 `World.IsAlive/Has/Get<CullState>`，可见性缓存有效；不能误报成 20K 次重复 cull 读取。

发生次数为每次完整投影 20,000 rays；静止且所有 revision 都不变时 `:98–105` 可以直接返回。当前有持续移动、变化或投影 revision 更新的采样帧走完整投影。复杂度为 `O(H × R)`，H 为可投影 owner/位置组合数，R 为高度图一次射线所遍历的地形格数；不是只做 20K 次矩阵乘法。稳态没有 GC，也没有 ECS 结构变更。

引入提交为 `0f1036ba53`。正确的遮挡功能需要保留。最小修复应将同一 owner 的遮挡锚点与两个 HUD 的屏幕布局分开，避免同一个单位做两次近似相同的地形判定；不同高度处的可见性差异必须明确定义并通过山脊边界样本验证。投影缓存 `:435–443`、`:531–539` 以 `owner.Id + 1` 倍增，无配置最大容量；增长时分配并复制。`:597–598` 的全数组清空只发生在 stamp 到 int.MaxValue，不能写成每帧清空。

可证伪方法：固定相机、20K 条目和地形，交替开启/关闭遮挡并计数；启用共享遮挡试验后，应看到 20K → 10K rays，且分别核对山脊前后、两个 HUD 锚点处的遮挡结果。如果时间没有随 rays 下降，或可见性不同，则该修复假设不成立。关闭遮挡的约 8–10 ms 差值是可消除工作上界，不能直接写成保留遮挡后的修复收益。

## 2. 首次采样前的单体创建留下 10K Presenter 逐帧行为资格

实际命中的完整调用链已由首个创建调用栈证实：`GameEngine.Tick` → `RealtimePacemaker.Update` → `PhaseOrderedCooperativeSimulation.Step` → `RuntimeEntitySpawnSystem.Update` → `SpawnTemplate:312` → `TryBootstrapPresenter:1638` → `PresenterEntityRuntime.CreateHierarchy:569` → `Create:337` → `AddBehaviorMarkers:1714` → `SyncTickBehaviorMarkers:1497` → `GroundingRequiresPresenterTick:1939` → `CanUseOwnerHeightmapSampleForSnapToGround:1958` → 添加 `PerfHasGrounding`。随后每个 presentation frame 的 `PresenterBehaviorSystem.Update` → `ProcessTickDrivenPresenters:670` 查询这些实体。调用栈保存在 [.audit-grounding-creation-ab.log](C:/001_AI/_audit_1485_ab/.audit-grounding-creation-ab.log) 的 196–211 行和 500–515 行。

- `src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:1958` 在 owner 尚未首次采样时令复用判断失败，`:1562` 添加 `PerfHasGrounding`。
- 同文件批量路径 `:430–447`、`:2009–2065` 也含 `Sampled == 0` 条件，但本次场景实际批量创建数为 0，不能用它代替实际调用链。
- `src/Core/Presentation/Systems/PresenterBehaviorSystem.cs:94–97` 是 tick query；`:670–728` 每帧遍历；`:2378–2389` 又逐单位验证已经采样；`:2415–2418` 对 owner 调 `World.IsAlive/Has/Get`。

六月日志 `behaviorTick=0`，当前精确版本 `behaviorTick=10000`。冷启动纯计数已确认首次采样状态决定了长期成员资格：10,000 个 agent root 在 `Sampled == 0` 时被加入 grounding query；之后 10,000 个 owner 全部已采样，marker 仍保留，循环内才跳过实际贴地。Query 已使用 Chunk/Span，不能写成“没有使用 Chunk”。每 visual frame 1 次查询、10K 次 owner 已采样检查；复杂度 O(N)。稳态 0Alloc，无需每帧结构变更。修复必须在创建规划阶段决定正确类型，不能改成每帧 Remove/Add 标记。

时间线：`d92aa08d69`（2026-07-10）给单实体与批量路径增加 `Sampled == 0`；`0a386d8cb02`（2026-08-30）只是更名。精确提交 `91618843a4` 的单实体 `:1958` 与批量 `:2059` 均仍含该条件。`PresenterBehaviorSystem` 相对此次 origin/main 没有差分，该问题被 #1485 保留。

根审计的反证已得到解释：只在批量 `:2059` 跳过 `Sampled == 0` 的实机对照仍得到每帧 `behavior_ticks=10000`、高度查询 10009、HUD 20000，因为本场景没有命中批量入口。冷启动纯计数来自 [.audit-grounding-qualification.log](C:/001_AI/_audit_1485_ab/.audit-grounding-qualification.log) 的 196–200 行：

| 计数 | 数值 |
|---|---:|
| 单体 root / child 创建 | 10,009 / 20,000 |
| 批量 root 创建 / 调用 | 0 / 0 |
| 单体 grounding 判断因未采样失败 | 10,000 |
| 单体其他失败理由 / 已可复用 | 0 / 0 |
| Sync caller：create / active / child / rebind | 30,009 / 0 / 20,000 / 0 |
| 同步时 grounding=true | 10,000 |
| 最终 grounding marker / 已采样 owner | 10,000 / 10,000 |
| 最终未采样或缺 sample 的 owner / 其他 tick marker | 0 / 0 |

MassNavigation 的十处模板均有 `ContinuousHeightmapSampleState`，light/heavy root 定义没有 graph/live/facing 绑定，Animator 也不在 tick query 的 WithAny 中；这些与计数相符。行为激活 `:1680` 和定义重绑 `:3243` 虽可重新判定，但本场景计数均为 0。

同版本 A/B 已完成：只改变单体 `:1958` 的初始采样状态谓词，仍检查组件存在、贴地兼容性与 transform source，首次 bootstrap 路径保留。每臂 10K 场景，达到生产投影状态后暖机 30 帧，再观测 60 个 headless production frame。关闭的是冗余的长期 grounding tick 资格，不是 Animator、HUD、持续 Effect 或 MassNavigation。

| 指标 | 原始资格 | 创建时允许复用尚未采样的 owner |
|---|---:|---:|
| Behavior 中位数 | 4.2691 ms | 0.0152 ms |
| Behavior 平均 | 4.331942 ms | 0.115052 ms |
| Behavior p95 | 6.3476 ms | 0.4183 ms |
| 每帧 tick 成员 | 10,000 | 0 |
| 每帧投影 HUD | 20,000 | 20,000 |
| 每帧核对 root 数 | 10,000 | 10,000 |
| root 与 owner 的 Y 最大差 | 0 | 0 |

数据：[audit-grounding-creation-ab.csv](C:/001_AI/_audit_1485_ab/audit-grounding-creation-ab.csv)。两组所有 60 帧均逐 root 核对高度，两个测试通过；此核对证明 owner 与 Presenter 在这些帧中保持一致，不能替代首次可见帧、地图切换、缺失采样组件、非零 grounding offset 等边界回归。当前是原始臂后 B 臂顺序执行的局部对照，未做交替次序重复；计数变化和高度一致性已证实，约 4.25 ms 的局部中位数差值仍须全功能整机复测后才登记为实际帧预算释放量。正式矩阵末尾基线总时间由约 56.31 ms 漂至 107.26 ms，更不能将跨版本 FPS 差值当收益。

## 3. 持续 Effect 的脏标循环仍发生 ECS 结构变更，并触发周期性托管分配

这是此次 PR 保留的整帧问题，三个源文件相对 origin/main 没有差分。没有改动 GAS transaction。300 帧暖机后，测 120 个 headless production frame（engine.Tick + camera + minimap + HUD，不含 Raylib/GPU）：

| 指标 | 数值 |
|---|---:|
| 总分配 median / p95 / max | 17,468 / 536,680 / 577,288 B/frame |
| 总分配平均 | 161,566.93 B/frame |
| 发生脏标移除的帧 | 37 / 120 |
| AttributeAggregateDirty 移除实体数 | 6,179 总计；每个处理帧 145–195 |
| GameplayAttributeChangedBits/TagChanged 移除实体数 | 6,216 总计；每个处理帧 146–196 |
| AttributeAggregator Playback 分配 | 9,058,560 B 总计 |
| ClearPresentationFlags Playback 分配 | 8,101,024 B 总计 |
| 两者 Playback 占总分配 | 约 88.5% |
| Gen0 / Gen1 | 3 / 1 次 |

逐帧数据：[audit-allocation-120frames.csv](C:/001_AI/_audit_1485_ab/audit-allocation-120frames.csv)。原始测试输出：[.audit-longalloc.log](C:/001_AI/_audit_1485_ab/.audit-longalloc.log)，测试 `Probe_A4F2_LongAllocationAndStructuralCounts`。不能将平均分配写成“每帧稳定分配 161KB”；多数帧低，约每 3–4 个已采样 visual frame 有一次脏标处理峰值。该窗口属于时间切片调度，处理帧中的 145–195 不是全部 10K 一次处理。

调用链与准确源码：

1. `GameEngine.Tick` → `PhaseOrderedCooperativeSimulation.Step` → `AttributeAggregatorSystem.Update` → inline job → `CommandBuffer.Remove<AttributeAggregateDirty>`（`src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs:259`）→ `Playback`（`:52`）。
2. 同一 simulation 调度 → `ClearPresentationFlagsSystem.Update` → inline jobs → `Remove<GameplayTagEffectiveChangedBits>`（`src/Core/Gameplay/GAS/Systems/ClearPresentationFlagsSystem.cs:44`）/ `Remove<GameplayAttributeChangedBits>`（`:55`）→ `Playback`（`:27`）。
3. `Arch.Buffer.CommandBuffer.Playback`（`src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs:369–395`）→ `World.RemoveRange`（`src/Libraries/Arch/src/Arch/Core/World.cs:1590–1620`）→ `World.Move`（`:325–347`）→ `Archetype.CopyComponents` + source `Remove`；最终逐组件 `Array.Copy`（`src/Libraries/Arch/src/Arch/Core/Chunk.cs:627–639`、`:658–662`）。

处理一次脏标即复制该实体其余组件到目标 archetype，并用原 archetype 末项填洞。复杂度 `O(D × C)`，D 为变化实体数，C 为实体组件列数；CommandBuffer 还按使用过的 component type 扫 sparse sets。这里使用 CommandBuffer 是正确的延迟方式，但重复将“有变化”编码成组件的存在/不存在仍是热路径结构变更。

结构变更的写入调用链为：`MassNavigationMod/assets/GAS/effects.json:16–17` 的 OnPeriod → `graphs.json:35` 的 `ModifyAttributeAdd` → `GasGraphRuntimeApi.cs:2166` 的 `StageAttributeAdd` → `EffectPhaseSideEffectTransaction.Commit:1296` 调 `AttributeMutationOps.SetCurrent` → `AttributeMutationOps.cs:47` 的 `MarkAttributeAggregateDirty` → `:215` 的直接 `World.Add<AttributeAggregateDirty>`。事务在提交前使用 CommandBuffer，但后面的通用属性写入仍可直接 Add。补充纯计数窗口中直接 Add 命中 aggregate=6940、presentation=42。`AttributeMutationOps` 这条标记逻辑由 `3c78fe5969`（2026-08-14）引入；移除脏标的既有逻辑可追至 `f4511b1631`（2026-04-26）与 `915c9b6616`（2026-04-29）。

补充纯计数已确认 Playback 分配集中在 hash miss 分支：`World.RemoveRange:1596` 用带 padding 的 `BitSet.Length` 构造查找 hash，而 `Component.GetHashCode(Span<uint>):441–445` hash 完整 span；`:1608` 查找失败后调用 `Signature.Remove`，在 `src/Libraries/Arch/src/Arch/Core/Query.cs:181–185` 创建 `HashSet` 与新数组，随后 `GetOrCreate` 用标准 signature 查找/创建 archetype。探针记录 13,922 次 RemoveRange、13,922 次 hash miss（100%），该分支的 `Signature.Remove + GetOrCreate` 合计分配 19,271,504 B，恰等于两个 Playback 的总分配；事件分发与 Move 各记录 0 B。当前计数没有将 Signature.Remove 与 GetOrCreate 进一步拆开，不能把整个分支字节全部单独命名为 HashSet 字节。哈希表示不一致是静态支持的原因，仍须通过统一 canonical hash 的独立 A/B 验证；不能只随意裁去尾零，因为标准 Signature 的 RequiredLength 在当前 net8 分支按 /31 计算。

补充窗口同样为 300 帧暖机、120 帧观测，保留独立数据，不覆盖前一窗口：

| 指标 | 补充窗口 |
|---|---:|
| 总分配 median / p95 / max | 22,324 / 523,784 / 563,672 B/frame |
| 总分配平均 / 总计 | 179,748.53 B/frame / 21,569,824 B |
| 发生脏标移除的帧 | 42 / 120 |
| aggregate / presentation 移除 | 6,940 / 6,982 |
| aggregate / clear Playback 分配 | 10,174,144 / 9,097,360 B |
| hash miss 分支占总分配 | 89.34% |
| 新 CellList 数 / 构造分配 | 1,283 / 595,312 B |
| SpatialPartitionUpdateSystem 总分配 | 1,003,104 B |
| Gen0 / Gen1 | 1 / 0 次 |

原始输出 [.audit-removerange.log](C:/001_AI/_audit_1485_ab/.audit-removerange.log)，逐帧数据 [audit-removerange-120frames.csv](C:/001_AI/_audit_1485_ab/audit-removerange-120frames.csv)。两组 median 均取排序后的第 60、61 项均值，p95 取 nearest-rank 第 114 项。两个窗口中的周期帧数量不同，不能把它们的平均差值解释为探针或功能带来的性能变化。

容量：CommandBuffer 默认初始 128，内部 PooledList、PooledDictionary、SparseSet/StructuralSparseSet 自动增长，无配置硬上限。超出既有容量会租用/分配、复制和填充；没有容量耗尽的显式业务错误。满 archetype 会自动创建 Chunk。修复方向是常驻变化位与紧凑 dirty 索引，按周期消费并清位；不得直接在 transaction 中重做已整合的事务索引优化。

底层时间线：`World.RemoveRange:1596–1611` 的这段代码可追至 `714f863d018`（2026-02-24）引入仓库的 Arch 实现，因此不是本次 PR 新增。分支还包含 `Query.cs:259–261` 的 Span 到 Signature 隐式转换，其构造器 `:59–63` 会 `ToArray`；不能把 `Span` 参数本身当成无分配保证。

## 4. 移动中进入新空间格子会构造 List 与 Dictionary

调用链：`GameEngine.Tick` → simulation → `SpatialPartitionUpdateSystem.Update:79–80` → `MoveJob.Update:420–426` → `SynchronizeTracked:517–518` → `ChunkedGridSpatialPartitionWorld.Add:33–44` → 内部 `Chunk.Add:126–136` → `new CellList:132`。

源码 `src/Core/Spatial/ChunkedGridSpatialPartitionWorld.cs:154–155` 每个首次使用的 cell 建一个容量 4 的 `List<Entity>` 和 `Dictionary<Entity,int>`。每次跨 cell 仍做 remove/add；移出空 cell 后不回收。容量 4 以上可继续增长。顶层 `_chunks` 也无最大容量，首次跨 chunk 在 `:38` 创建 chunk 及 cell 引用数组。

上述 120 帧窗口中，1,217 个新 CellList 分配 564,688 B（每个 464 B）；28 帧出现新 cell，单帧 31–59 个。SpatialPartitionUpdateSystem 总计 998,040 B，其余约 433KB 不能全部归因 CellList 构造，包含容器增长等尚未拆分部分。lookup 和 cell 内 remove 已有 Dictionary，不能报告线性查找；平均 O(跨格单位数)，新容量增长时 O(已有容量)。这条路径本身不搬 ECS archetype，除非缺少 SpatialCellRef 的初始化路径被执行。

可证伪 A/B：固定移动轨迹，A 首次进入新格；B 在正式采样前预热同一轨迹覆盖的格。比较新 CellList 数与分配；若 B 仍重复构造，需检查空间系统重建或不同轨迹。正式修复需要配置 cell/chunk/成员池容量，预分配后显式报满，保留完整 Entity 身份和 O(1) swap remove。

## 5. Transform sync 的全扫描与跨实体读取仍有固定成本

`src/Core/Presentation/Systems/PresenterEntityTransformSyncSystem.cs:61` 每帧扫 owner payload；`:195–215` 遍历 10K owners，逐个验证 root 的 6 个组件；`:232–243` 获取 state 与变换列。只要任一个 root 变化，`:121–124` 就触发全部附着子级查询；`:278–324` 扫 20K children 并每 child `World.Get` parent 的四个变换组件。`SyncFastAttachedChildren:363–373` 还访问 children，遇到新 fast marker 才跳过。

调用链：`GameEngine.TickPresentation` → `PresenterEntityTransformSyncSystem.Update` → `SyncSingleRootOwnerPayloads` → root dirty → `SyncOwnerPayloadAttachedChildren` → `ApplyFastParentAttachment` → `PresenterEntityRuntime.MarkStaticDirty` → retained dirty buffer → `PresenterEmitSystem`。

10K 世界中，即使只移动一个 owner，也可能承担 `O(N + H)` 扫描；全静止时 child 扫描可跳过但 owner 扫描仍存在。稳态 0Alloc，无 ECS 结构变更。既有移动数测试把世界固定为 10K：1K/5K/10K moving 分别 sync 2.544/3.273/2.934 ms、emit 2.114/2.966/4.279 ms，0 B/frame。这不是 1K/5K/10K 整机规模对比。此数据支持较大的固定扫描成本，不能说明 5K 比 10K 天生更慢。

`ac8ab86af0` 增加该快路径和 position-only HUD 更新，属于改善。它不能被认定为回退引入提交。A/B 应固定 10K 世界、相机和血条变化，分别移动 0、1、1K、10K owners，计数实际 root/child 访问；试验维护 owner/root/child 的稳定紧凑索引并只传播 dirty roots。预计收益尚未验证。

## 6. Emit 与缓冲区审计边界

`PresenterEmitSystem.cs:105–165` 使用 Chunk/Span，动态 skinned 逐帧完整输出 10K 个 transient（`:1583–1604`），复杂度 O(N)。`SkinnedVisualBatchBuffer.Clear` 仅重置 count，不能误报每帧清空整个数组。HUD dirty 路径通过 `ReadOnlySpan<Entity>` 消费（`:232–240`）；`PresenterEntityRuntime.cs:4660` 每次消费后清当前 D 个 Entity，O(D)。dirty buffer 在 `:5184–5189` 从 256 开始倍增，第一次增长分配，但稳态局部测试为 0 B/frame。

容量配置来自 `mods/capabilities/navigation/MassNavigationMod/assets/game.json:17–33`：Presenter/HUD/skinned 主容量为 131072。底层 TryAdd 在满时返回 false并计 drops；本次 skinned 快路径 `PresenterEmitSystem.cs:1606–1607` 和 retained HUD 快路径 `:526–527` 明确抛错，因此不能将这两条生产快路径写成静默丢失。dirty scratch、replay Dictionary、pendingDestroy List、grounding scratch 则没有独立配置上限。

`PresenterEmitSystem.cs:171–177` 会在寿命结束后调用 runtime.Destroy；`PresenterEntityRuntime.cs:638–672` 递归销毁并直接 `_world.Destroy`。它发生在 query 结束后，不会在当前 chunk 内销毁，但不是 CommandBuffer；10K 持续展示采样没有证实有这条寿命回收工作，应列为合同风险而非 FPS 根因。`PresenterBehaviorSystem.cs:190–207` 已包 deferred changes；bootstrap/material 脏标移除在 `:425`/`:846` 入 CommandBuffer，并于 `:855` 回放。这些标记在创建或变化时有结构成本，稳态当前样本没有逐帧 material/bootstrap 成本证据。

AoS 仍存在于输出 DTO：`WorldHudItem.cs:6–22`、SkinnedVisualBatchItem 及 owner cache entries。ECS 主查询的组件列是 SoA。当前证据证明 HUD 射线重复工作和 tick 成员资格问题，尚未证明 DTO 布局单独造成多少 ms；不能因 `struct[]` 存在就宣布它是性能根因。SoA 改造需保持相同 workload 另做布局 A/B。

Query、LINQ 与重复初始化：这四个目标主热路径没有每单位 LINQ/闭包创建；定义注册变化时 `PresenterBehaviorSystem.RefreshDefinitionIndexes:242–253` 会 List/ToArray/Dictionary 重建，这是版本变化时工作。Frame graph 寄存器/scratch 是持有数组。未发现目标主路径的 `N×障碍数` 或 `N×N` 无界扫描。

## 最小修复顺序与预算约束

1. 修正单体创建的长期 grounding 查询成员资格，保留首次贴地合同。同版本局部 A/B 已测到约 4.25 ms 的中位数差值，整机收益和边界回归尚需复核。
2. 共享每 owner 的地形遮挡工作，保留实际投影差异和遮挡正确性。关闭遮挡所得 8–10 ms 是上界；共享一次最多直接减半这部分射线数，实际收益需实测。
3. 消除周期脏标 Add/Remove；为已测得的 RemoveRange hash miss 分支分配补 canonical hash A/B 与边界测试。不修改 EffectPhaseSideEffectTransaction。
4. 维护 dirty root/child 传播索引，避免少量移动触发 20K child 扫描；然后处理空间 cell 与 scratch 的配置容量。

现阶段不能把以上预算简单相加：行为修复可能改变投影 dirty 数，Effect 周期帧也影响分配与 archetype 分布；必须保持全功能重复整机矩阵，重新报告剩余帧时间。

## 实验边界与复现

所有源码行号指向未改动的 `C:/001_AI/LudotsAudit10k` 精确提交。实验树 `C:/001_AI/_audit_1485_ab` 的变动为：逐系统 allocation observer；aggregate/clear 的移除数与 Playback bytes；AttributeMutationOps 的 Add 命中数；空间 cell 构造数/bytes；Arch RemoveRange 分支 bytes；HUD 射线与局部共享试验；Presenter 冷启动资格计数及单体创建 A/B；生产路径测试入口。`PresenterEntityTransformSyncSystem` 的 25 行是根审计之前加入的 counter/reset，只观察已有路径。HUD owner-shared 试验在本附件的投影、整帧分配和 grounding 测试中均显式设为 false。单体创建 A/B 开关默认 false，仅对应测试的 B 臂开启，测试 finally 复位。

构建：`dotnet build src/Tests/PresentationTests/PresentationTests.csproj -c Release --no-restore`。测试均用 `dotnet test src/Tests/PresentationTests/PresentationTests.csproj -c Release --no-build --filter FullyQualifiedName~<测试名> --logger "console;verbosity=normal"`，各日志保留在实验树。探针只测当前线程分配；Gen0/Gen1 是该测试进程在窗口中的 collection count 差值。局部投影测试不运行每帧 simulation；整帧分配与 grounding 测试运行 headless production Tick，不含 Raylib/GPU。没有将这些测试转换为整机 FPS。
