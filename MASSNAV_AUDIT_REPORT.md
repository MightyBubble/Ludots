# MassNavigation 全链路审计报告

审计基线：`origin/main` / `81a1b5f543`，工作树：`.worktrees/massnav-audit`。

审计范围覆盖 `src/Core/MassNavigation` 下 40 个运行时文件、MassNavigation 相关 Physics2D/GAS/Movement/Input 桥、MassNavigationMod、10K showcase、配置资产以及 MassNavigation 测试。重点按调用链逐文件检查：配置和容量合同、runtime 激活、实体绑定、SoA solver、环境障碍、组目标、MovePlan/route sink、PoseAuthority、展示与用户输入。

## 已复现问题

### P0：10K showcase 右键下单完全不可用

复现命令：

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter FullyQualifiedName~MassNavigation
```

结果：114 个测试中 113 通过，1 个失败：`CapabilityStandardMassNavigationLargeWorld10kProductionPathTests.Showcase_MouseBoxAcquisition_RightClickIssuesOrdersForCommandableAgents`。

实际状态：2001 selected、10000 visible、2500 eligible、1989 intersecting、2001 command sources，但右键后 `CommandCountFrame == 0`，日志为 `ORDERSRC bind failed actor=Entity = { Id = 0, WorldId = 0, Version = 0 }`。

根因链：`MassNavigationLargeWorldLocalOrderSourceSystem` 每帧要求 `GetControlledActor()` 成功后才给 mapping 绑定 seat actor；`LocalOrderSourceHelper.GetControlledActor()` 只从本地 possessed player 解析受控 actor；scenario bootstrap 为生成单位只写 `OwnershipSource`（来自 `MapConfig.Players`），并未保证该玩家有匹配的本地 controlled actor。结果单位已经可选、可成为 command source，但输入映射没有 actor，整条下单链被拒绝。

证据：

- `mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod/Systems/MassNavigationLargeWorldLocalOrderSourceSystem.cs:39-60`
- `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs:143-150, 387-405`
- `src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs:166-195`
- `src/Tests/PresentationTests/MassNavigation/CapabilityStandardMassNavigationLargeWorld10kProductionPathTests.cs`

影响：showcase 的核心用户动作“框选部队 → 右键移动”不可用，属于产品级阻断。

### P1：复合障碍物导致环境绑定每帧重建

`ComputeSignature()` 把每个障碍 projection 的 `PieceCount` 累加到 `BlockerCount`；`BindBlockers()` 却对每个障碍实体只调用一次 `RegisterBlocker()`。一个两片障碍的签名计数为 2，运行时计数为 1，快速路径永远不成立。之后每帧清空并重建障碍、写入 `MassNavigationBlockerProfile`，并触发结构变更。

证据：`src/Core/MassNavigation/Systems/MassNavigationEnvironmentBindingSystem.cs:96, 137-178`；计数定义：`src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs:64-67`。

影响：复合障碍场景持续结构写入和 flow rebuild，破坏增量绑定，规模越大越严重。

### P1：路由 sink 接受不一致 agent index 和无效预算

`ValidateRouteTarget()` 只检查实体存活、profile、目标坐标和 `maxPoints`，不验证：

- `agentIndex` 是否等于实体上的 `MassNavigationAgentIndex.Value`；
- `agentIndex` 是否等于 simulation binding；
- `requestId > 0`；
- `maxExpanded` 是否不超过配置容量（路径服务把 `maxExpanded <= 0` 解释为无限预算）。

后续 `TryApplyTrackedRouteTargets()` 用未验证的 `state.AgentIndex` 调用 solver，可能把路径写给错误单位，或在越界时异常。

证据：`src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs:230-296, 660, 727, 817`。

影响：错误请求不会在入口被拒绝，可能出现错单位移动或运行时崩溃。

### P1：PoseAuthority 提交不是原子操作

`CommitPendingTransitions()` 先回放全部 ECS `PoseAuthority` 写入，再逐个更新窗口并调用 listener。`MassNavigationPoseAuthorityBridge.MarkAgentDisplaced()` 在 displaced 容量不足、索引失效等情况下会抛异常。此时 ECS 已经部分或全部切换到 `Displacement`，但部分 listener 尚未执行，pending 列表也未清理，solver displaced 集合与 ECS 权威不一致。

证据：`src/Core/Movement/PoseAuthorityArbiter.cs:414-461`；listener：`src/Core/MassNavigation/Systems/MassNavigationPoseAuthorityBridge.cs:42-87`。

影响：一次容量/生命周期错误会留下半提交位姿权，后续固定步可能持续异常。

### P1：环境身份只使用 Entity.Id，不使用 Version

环境签名和已生成实体集合都只混入/存储 `Entity.Id`。Arch 回收实体 ID 后会递增 Version；新一代障碍或 marker 可能命中旧签名或被认为已经跟踪。

证据：`src/Core/MassNavigation/Systems/MassNavigationEnvironmentBindingSystem.cs:97`；`src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs:26, 221-226`。

影响：实体销毁后重生的障碍/标记可能不触发绑定更新，造成过期环境数据。

### P1：Reset 在清除旧障碍前计算队伍目标

`MassNavigationFlowSolverState.Reset()` 先 `InitializeTeams()`，后 `ClearRuntimeObstacles()`。`InitializeTeams()` 内的 `ResolveNavigableTarget()` 使用 solver 当前仍缓存的旧障碍布局；复用 solver 且障碍变化时，队伍目标会按上一局障碍投影。

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs:344-371, 1198-1203`。

影响：切图/重建/障碍变化后初始队伍目标可能被错误偏移。

### P1：未知 team 更新后保留旧 team runtime/flow 索引

`SetUnitRuntimeProfile()` 先写 `_teams[index] = teamId`，只有 `_teamStateIndexById.TryGetValue(teamId)` 成功时才更新 `_teamRuntimeIndices[index]`。未知 team 会暴露新 team ID，却继续使用旧 team runtime 和旧 flow 状态。

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs:474-543`。

影响：关系判断、目标、流场和避障使用错误队伍；错误可能持续到下一次完整重建。

### P1：可控 agent slot 使用完整 agent index，计数却是紧凑计数

`RegisterAgentAtIndex()` 用 `agentIndex` 扩张 `_controllableAgents` 并写入；`_controllableAgentSlotCount` 只递增 1。若 agent 0 不可控、agent 1 可控，slot 0 是 Null、count 是 1，`TryGetControllableEntity(0)` 返回失败。

证据：`src/Core/MassNavigation/Runtime/MassNavigationAgentState.cs:81-90, 139-164`。

影响：控制集合、命令源或 UI 读取可控 slot 时会漏单位或读到空实体。

### P1：组到达只看中心，不看成员

`MassNavigationGroupRuntime.UpdateTargets()` 计算成员中心到 destination 的距离，并以 `ArrivedRadiusCm` 设置 `group.Arrived`；没有检查每个成员的目标距离或 settled 状态。一个成员停滞、卡在障碍后，中心仍可能进入半径，命令会提前完成。

证据：`src/Core/MassNavigation/Runtime/MassNavigationGroupRuntime.cs:585-617`。

影响：MovePlan 结果会报告全组 `Arrived`，用户看到仍未到位的单位。

### P1：障碍旋转可在无 dirty/motion 时不落地

Physics2D bridge 的 static query 只处理新实体、显式 dirty 实体或带 `ManifestationMotion2D` 的实体。虽然 `ComputePoseSignature()` 包含 `FacingDirection`，但静态实体旋转后若没有同步添加 dirty 标记，`MassNavigationFlowObstacleProjection` 不会刷新。

证据：`src/Core/Ludots.Physics2D/Systems/ManifestationObstacleBridge2DSystem.cs:18-41, 89-105, 468-478`。

影响：物理姿态与 MassNavigation 障碍投影分叉，旋转后的箱体/多边形仍按旧方向导航。

### P2：负坐标 hash cell 向零截断

`TryGetHashCell()` 直接把 `positions * invHashCell` 转成 `int`。C# 对负浮点向零截断，例如 `-0.5f -> 0`，而不是 floor 的 `-1`。solver window 边缘的负局部坐标会错误落入 0 号 bucket。

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverSpatialHash.cs:72-91`。

影响：窗口边缘负坐标单位可能参与错误邻域查询，出现漏避障或错误硬解析。

### P2：归一化阈值合同不完整，可能产生零方向

`SafeInverseSqrt()` 在 `value < InverseSqrtMinValue` 时返回 0；调用点有的用 `DirectionEpsilonSq`，有的用 `NormalizationEpsilonSq`。配置校验只要求三个阈值为正，没有约束它们之间的可用顺序。可配置出“分支认为需要归一化，但 SafeInverseSqrt 返回 0”的组合。

证据：`src/Core/MassNavigation/Runtime/MassNavigationFlowSolverState.cs:2772-2775`；`src/Core/MassNavigation/Runtime/MassNavigationCrowdSemantics.cs:318-326`。

影响：期望的单位方向可能变成零向量，导致停滞或避障方向异常。

### P2：单一输入源对未知场景 team 的 ownership 只返回 Null

`ResolveScenarioTeamControlOwner()` 没有匹配 `MapConfig.Players` 时返回 `Entity.Null`，spawn 继续进行。错误直到 input/command source 阶段才暴露，且 bootstrap 没有报告哪个 team 缺少控制映射。

证据：`src/Core/MassNavigation/Systems/MassNavigationScenarioBootstrap.cs:166-195`。

影响：配置错误在启动时不 fail-fast，showcase 进入“单位存在但无法下单”的半可用状态。

## 设计/容量风险

1. `ValidateForScenario()` 只验证全场 authored agent 数不超过 `GroupMemberCapacity`、`MovePlanExecutionMemberCapacity` 等；它没有验证 route capacity 是否覆盖可能同时路由的成员数，也没有验证 `DisplacedAgentCapacity <= authoredAgentCount`（后者不是错误，但容量语义不一致）。
2. `MassNavigationMovePlanExecutionSystem` 在准备阶段先改变 focus 预览状态、再提交组；异常恢复依赖 `FocusState` 快照，组/route sink 另有自己的事务，跨对象并非单一事务。
3. `MassNavigationAuthoredAgentBindingSystem` 通过实体 ID 排序稳定索引，正确避免 archetype 顺序漂移，但重建会清除 displaced 状态并取消所有 pose windows；这是强制性全局中断，调用方若把重建视为增量变化需要额外处理。
4. `MassNavigationFlowSolverState` 的 parallel step 共享只读快照并按 agent 独占写槽，SoA/0Alloc 热路径方向正确；但所有 capacity 仍允许在 cold path `Array.Resize`，调用方必须保证运行时不触发扩容。
5. `MassNavigationRuntimeBinding` 的 ready gate 设计清晰，但 `MassNavigationRuntime.ReleaseMapState()` 只在卸载时清空 authored runtime；挂起后再次激活依赖原实体仍在世界中，生命周期顺序必须保持不变。

## 已检查且当前未发现直接缺陷的区域

- `MassNavigationRuntimeBinding` 的 revision/prepared gate；
- authored agent 扫描、实体 ID 稳定排序、关系 projection revision；
- displaced agent 的 solver 跳过积分、回灌世界位姿、交还后 reset recovery；
- group/route 的 prepare → preflight → commit 主流程；
- flow/separation/hard-resolve 的 SoA 数组和并行 scratch 预分配结构；
- strict config top-level/property 校验、统一 AgentProfile geometry 引用；
- MassNavigationMod 的地图 gate、资产/模板/presenter 合同；
- 现有 MassNavigation 单元、合同、位移、组事务、route 与 showcase 测试覆盖。

## 验证结论

MassNavigation 核心 solver 的主要结构符合 SoA、预分配和固定步边界要求，但外围契约存在多个高影响断点。当前最紧急的是 10K showcase 下单链路；其次是环境绑定计数、route 输入校验、PoseAuthority 原子提交和实体版本身份。上述问题均可在不改变 solver 主算法的前提下，通过入口契约和状态提交修复。
