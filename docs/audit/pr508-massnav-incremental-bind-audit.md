# 审计报告 — MassNavigation authored-agent 增量绑定（Issue #502 / PR #508）

- 审计对象：`cursor/massnav-incremental-agent-bind-0518` @ `ae13d7451`
- 基线：`e8e6f0c00`（PR 只含 2 个 commit：`bedb1ae4d`、`ae13d7451`）
- 变更文件：4（`FlowSolverState.cs` +77、`SimulationRuntime.cs` +34、`MassNavigationAuthoredAgentBindingSystem.cs` 重写、新增增量测试 298 行）
- 验证：`dotnet test ...~MassNavigationAuthoredAgentBindingIncrementalTests` → **通过 5/5**（2 秒）

---

## Executive summary

**结论：可以合并 PR #508。** PR 精准实现了 issue #502 的核心诉求——纯插入路径不再全量 rebuild、不再清空 `NavGroupRuntime`/order/target，只 append 新 agent。append 路径沿正式 `RuntimeEntityBinding` 链路，SoA 语义完整，稳态帧收敛到单次 ECS 扫描、0 分配，ID（`MassNavigationAgentIndex`）在 append 下严格稳定（尾部追加，不重排已有 index）。

**唯一未闭合的 AC 是 #502 第 2 条**：当 append guard 不成立（删除 / 属性变更 / 增删混合）时仍走 `RebuildFromAuthoredAgents`，该路径**只 snapshot/restore selection，不 snapshot group/order**（`SimulationRuntime.cs:722` → `ClearAuthoredRuntimeBindings` → `NavGroupRuntime.Reset()`）。这一点 PR description 已诚实标注为「Remaining follow-up (tracked on #502)」，未伪装成已解决。因此这不是「PR 谎报」问题，而是**范围切分**：#502 仍需保持 open，AC#2 未勾选。

我不建议把 AC#2 塞进本 PR。full-rebuild 的 group/order snapshot/restore 是一个独立、更复杂的工作（order token / formation anchor / per-unit target 的按存活 agent 重映射），与本 PR 的「纯插入 append」正交，合并后迭代更安全。

---

## AC checklist（Issue #502）

| # | AC | 状态 | 证据 |
|---|----|------|------|
| 1 | 新增 authored agent 能绑定而不 reset 无关 NavGroupRuntime | ✅ 满足 | `SimulationRuntime.AppendAuthoredAgents` (`:733-765`) 不触碰 `NavGroupRuntime`；`FlowSolverState.AppendAuthoredAgents` (`:359-434`) 只写尾部 index。测试 `AppendAuthoredAgents_PreservesActiveMoveGroupState` / `AuthoredAgentBindingSystem_IncrementalInsert_PreservesActiveMoveGroupState` 断言 group/order target 不变。 |
| 2 | 无法避免 full rebuild 时 snapshot/restore 存活 agent 的 group/order | ❌ **未满足** | `RebuildFromAuthoredAgents` (`:701-731`) 仅 snapshot selection（`:712-720`, `:729`），group/order 经 `ClearAuthoredRuntimeBindings`→`NavGroupRuntime.Reset()` (`:695`) 丢失。PR 已声明为 follow-up。 |
| 3 | selection restore 确定性、不掩盖 group/order 缺失 | ⚠️ 部分 | `RestoreSelectionAfterAuthoredRebuild` (`:767-794`) 只按 `TryGetControllableIndex` 过滤存活 controllable，确定；但恰因为它「restore 了 selection 却没 restore group」，在 full-rebuild 分支仍会给用户「选中还在、但队列/目标没了」的观感——这正是 AC#2 未闭合的表征，非 append 路径缺陷。 |
| 4 | 回归测试：建 active move group → 插入新 agent → 验证 group/order 稳定 | ✅ 满足 | 两个 Preserves 测试覆盖「runtime 直接 append」与「binding system 经 ECS 扫描 append」两条入口。 |
| 5 | 容量溢出显式失败，不静默截断 | ✅ 满足 | `AppendAuthoredAgents` (`:749-755`) 对 `groupMembershipAgentCapacity` `checked+throw`；测试 `AppendAuthoredAgents_ExceedsMembershipCapacity_Throws` 断言异常含 `groupMembershipAgentCapacity`。`FlowSolverState` 用 `checked(startIndex+len)` (`:367`)。 |

---

## 真问题 / 伪问题 / 可接受冷路径

### 真问题（必须知晓，但均非本 PR 阻断项）

1. **[已知/已声明] full-rebuild 不 snapshot group/order（AC#2）** — `SimulationRuntime.cs:722`。影响：删除一个 soldier、或改一个 agent 的 profile/team/layer 会触发全量 rebuild，仍会清空所有活跃移动队列。issue repro（RallySoldiers 纯刷兵）已被 append 覆盖，但「刷兵 + 同帧死人」会退回 rebuild。→ **follow-up，非阻断**。

### 伪问题（审计中排除）

- **append 漏初始化某个 SoA 数组** — 排除。append 未逐一写 `_maxInteractingBodyRadiiCm` / `_separationHashSearchRadiusCellsByAgent` / `_hardResolveHashSearchRadiusCellsByAgent`，但设置了 `_maxInteractingBodyRadiiDirty=true`（`FlowSolverState.cs:432`），`RecomputeMaxInteractingBodyRadiiCm` (`:2391-2424`) 会对 `[0,UnitCount)` 全量重算这三者。`EnsureCapacity` (`:1159-1165`) 已 resize。无脏读。
- **`_teamStates[i].UnitCount++` 结构体拷贝失效** — 排除。`TeamRuntimeState` 是 `sealed class`（`MassNavigationFlowSolverRuntimeState.cs:45`），引用类型，就地可变，append 的 team-local-index 续号正确。
- **append 后 team relationship matrix 陈旧** — 排除。新 team 分支置 `_teamRelationshipRevision = int.MinValue`（`:383`），`RefreshTeamRelationshipMatrixIfStale` (`:1674-1707`) 下一步会重建并按 `teamCount²` 扩容。
- **follower sync cache 在 append 后 stale（#214 边界）** — 排除，且这是本 PR 的**正确修复**。append 不 bump `AuthoredRuntimeBindingRevision`（append 不重排 index），`MassNavigationFormationFollowerSystem.InvalidateSyncStateForRuntimeLifecycle` (`:107-118`) 因此不再误清无关 formation 的 carrier/target snapshot。测试 `..._DoesNotBumpAuthoredRuntimeBindingRevision` 锁定该语义。full rebuild 仍走 `MarkAuthoredRuntimeBindingChanged` (`:698`)，follower cache 正确失效。

### 可接受冷路径

- append/rebuild 位于 `SystemGroup.RuntimeEntityBinding`（冷路径）。分配仅限：`_entities/_seeds/_controllableFlags` List 复用（`Clear` 后 append，稳态不增长）、`EnsureCapacity` 的 `Array.Resize`（仅超容时）、新 team 时 `_teamRelationshipMatrix` 重 new。均可接受。

---

## ID 漂移矩阵（`MassNavigationAgentIndex` = dense solver index vs `Entity.Id`）

| 场景 | 触发路径 | 已有 agent index | 结论 |
|------|----------|------------------|------|
| bootstrap（scenario 首次绑定） | `RebuildFromAuthoredAgents` | 从 0 建立 | 基线，无漂移 |
| 批量创建（RallySoldiers 刷 8 兵，signature 稳、只增 unbound） | `TryAppendUnboundAuthoredAgents`→`AppendAuthoredAgents` | **稳定**（尾部追加 `startIndex+i`） | ✅ 无漂移，issue repro 已闭合 |
| runtime 单个 spawn | 同上 append | **稳定** | ✅ 无漂移 |
| agent 删除 / 属性变更 / 增删混合 | append guard 失败 → `RebuildFromAuthoredAgents` | **全部重排**（按 ECS query 顺序从 0 重编） | ⚠️ index 漂移是设计内的，但 group/order 未随之重映射 → AC#2 |

**index 稳定性关键校验**（append 分支）：
- `AppendAuthoredAgents` 用 `startIndex = MassNavigationFlow.UnitCount`（`SimulationRuntime.cs:757`），逐个 `BindSpawnedAgent(..., startIndex+i, ...)`（`:761`）。
- `BindSpawnedAgent` (`:1150-1185`) 断言 `agentIndex < UnitCount`、且实体未重复绑定，`AgentState.RegisterAgentAtIndex` (`AgentState.cs:124-164`) 对已占用 index 抛异常 → 追加语义被强校验。
- 下游消费方 **不缓存过期 index**：`MassNavigationLocomotionAnimatorParamSystem` 每帧 `World.TryGet<MassNavigationAgentIndex>(owner)` 现读（`:55-63`）；`MassNavigationAgentMetadataSyncSystem` 从 ECS chunk 现读（`:70`）。Presenter scope 用 `OwnerEntity` 而非 solver index（符合 formal-chain「不要用 solver index 做 presenter scope」铁律）。
- `_groupIdsByAgentIndex`（`GroupRuntime.cs:16`，按 agent index keyed）在 append 下不失效：新 index 是尾部新槽，`EnsureMembershipCapacity` (`:678-687`) 边界是 `GroupMembershipAgentCapacity`，与 append 的容量校验同源。**唯一风险仍是 rebuild 分支的重排**——但 rebuild 已 `NavGroupRuntime.Reset()`，故不存在「index 漂移但 group 表未更新」的悬垂。

---

## SoA / 0Alloc / 热路径评语

- **`FlowSolverState.Step` 热路径**：本 PR 未改 Step；append 只扩尾、置 dirty，Step 仍 SoA、无 GC。✅
- **`AppendAuthoredAgents`（FlowSolverState）**：纯尾部顺序写 SoA 数组（`:387-429`），`checked` 溢出保护，`MarkEntityDirty` 逐个入队保证新 agent 首帧 writeback。✅
- **`BindingSystem.Update` 稳态帧**：`ScanAuthoredAgentBindingState` (`:135-180`) 单次 `Query(AuthoredAgentsQuery)`，用 `chunk.Has<MassNavigationAgentIndex>()` 按 archetype chunk 一次性区分 bound/unbound，同时算 authored 与 bound 两个 signature。相比旧版 `CountAuthoredAgents`+`HasUnboundAgent`+`ComputeAuthoringSignature` 三次扫描，收敛为 **1 次扫描、0 分配**（early-return 在 `:66-71`）。✅
- **append/rebuild 冷路径**：分配可接受（见上）。✅

---

## 与 #214 边界

#214 修的是 reset/rebuild 下 MassCrowd follower sync cache 陈旧。本 PR 的 append **不 bump** `AuthoredRuntimeBindingRevision`，故不会像 rebuild 那样清 follower cache——这是**正确**的，因为 append 不改已有 index，已有 formation 的 carrier/target snapshot 仍有效。full rebuild 分支保留 `MarkAuthoredRuntimeBindingChanged`，#214 的失效条件不变。**未引入新 stale cache**。✅

---

## 分级建议

### 必须修（合并前）
- 无。功能正确、测试绿、无回归、无架构越界。

### 建议 follow-up（合并后，挂 #502 继续）
1. **AC#2**：为 full-rebuild 分支实现 group/order snapshot/restore（按存活 agent 重映射 order token 与 per-unit target）。这是 #502 唯一未闭合 AC。
2. **AC#3 强化**：在 full-rebuild 且无法 restore group 时，考虑是否应连 selection 一并清（避免「选中在、队列没」的割裂观感），或明确文档化该行为。
3. **metadata team guard**（PR 已列 follow-up）：runtime-spawn 的 team 不在 `scenario.teams` 时，`MassNavigationAgentMetadataSyncSystem` (`:105-111`) 会 throw。RallySoldiers 若刷出新 team 会硬失败——需确认 HAN 刷兵 team 已在 scenario.teams，否则 append 路径本身会被 metadata sync 拦截。**建议补一条集成测试或在 #502 明确此前置条件**。

### 可合并后迭代
- append 分支的 List scratch 可进一步复用/池化（当前已 `Clear` 复用，收益有限，低优先）。

---

## 对 Issue / PR 文本的更新建议

- **Issue #502**：保持 open。评论补充：AC#1/#4/#5 已由 PR #508 闭合并有回归测试；**AC#2 未闭合**（full-rebuild snapshot/restore 未做），AC#3 因 AC#2 部分留白。追加前置条件说明：runtime-spawn agent 的 team 必须已在 `scenario.teams`，否则 `MassNavigationAgentMetadataSyncSystem` 抛异常。
- **PR #508 description**：无需修改——已诚实区分「incremental append 已解决」与「full rebuild snapshot/restore = Remaining follow-up」，并如实列出 metadata team guard 为 follow-up。description 与代码一致。

---

## 附：必跑验证输出
```
dotnet test src/Tests/PresentationTests/PresentationTests.csproj \
  --filter FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests
→ 已通过! 失败:0 通过:5 跳过:0 总计:5 (2s)
```
覆盖：runtime append 保 group、binding-system 增量插入保 group、append 不 bump binding revision（且 StructuralChange > 0）、容量溢出显式 throw。
