# GAS Composition Gate — Entity Attachment（#1064）

- **Task / Issue**: Entity Attachment：绑定触发 + 位置同步 sink（#1064）
- **Date**: 2026-08-23
- **Agent / Author**: codex/entity-attachment worktree

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: attach/detach 是新增 Layer 0 原子 op（挂在既有 `ApplyRelation` 的 `RelationOperation` op 枚举上，与 `SetParent`/`RemoveParent`/`EnsureLink` 同构），落位策略是 op 参数（`DetachPlacement`，先例：`DisplacementDirectionMode`）；消费端是单一 ECS 系统，无新 profile DSL、无平行管线。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| Attach op（ChildOf + AttachedLocalPose + 写权授予 + 初始派生位姿 + 环/容量校验） | 0 | `RelationOperation.Attach` + `EffectPhaseSideEffectTransaction.StageAttach` + `AttachmentOps.Attach` |
| Detach op（解绑 + 写权归还 + 落位策略参数） | 0 | `RelationOperation.Detach` + `StageDetach` + `AttachmentOps.Detach` |
| RemoveParent 事务对称化 | 0 | `StageRemoveParent`（补上既有非事务路径的事务化） |
| attach/detach all-or-nothing + rollback | 1 | `EffectPhaseSideEffectTransaction` 既有事务壳扩展 |
| 挂接/卸下编排（谁在何时 attach、链上接哪些 effect） | 2 | effect template（`GAS/effects.json`）+ 既有 graph 线，本票不新增 runtime 解释器 |
| 位置同步 | —（非 GAS，ECS 系统） | `AttachmentPositionSyncSystem`（PostMovement 组，`WorldToGridSyncSystem` 之前） |
| `EntityTemplate.children` 预置组合 | authoring | `RuntimeEntitySpawnQueue` 既有 spawn 管线扩展（无第二条物化管线） |

## 3. Reuse list

- Handlers: `BuiltinHandlers.HandleApplyRelation`（Attach/Detach/RemoveParent 分支并入，不新建 handler id）
- Queues / Systems: `RuntimeEntitySpawnQueue`/`RuntimeEntitySpawnSystem`（`TryApplyParentLink` 先例扩展为父子批量 enqueue）、`SavePreviousWorldPositionSystem`、`WorldToGridSyncSystem`、`PoseAuthorityCommitSystem`
- Resolvers / Registries: `EffectTemplateRegistry`（RelationDescriptor 扩展字段）、`EffectTemplateLoader`（严格解析）、`ComponentRegistry`（`AttachedLocalPose` 注册）、`EntityTemplateKeyRegistry`
- 既有事务基建: `EffectPhaseSideEffectTransaction` 的 relation 段（ChildrenBuffer/ChildOf/位姿快照/结构命令回滚）与 `PoseAuthorityArbiter` 的 pending-transition 边界结算
- 既有跟随先例: `ManifestationMotion2DSystem`（迁移源，职责并入通用 sink）、RTS `RtsRelationRuntimeSystem`（周界散布落位先例，迁移到 Core API）

## 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| `RelationOperation.Attach` | 建立绑定（ChildOf+AttachedLocalPose+写权授予+初始派生位姿，快照供 detach 恢复） | SetParent 只建组件边，不处理 AttachedLocalPose、写权归属与对称快照；组合 SetParent+多个假想 op 反而需要跨 op 隐藏状态传递 |
| `RelationOperation.Detach` | 解除绑定（写权归还+按参数落位） | RemoveParent 只拆边；落位与写权归还必须与拆边原子回滚 |

（sink 与 authoring 扩展不是 op，不在此表）

## 5. Transaction boundary

必须原子 rollback 的步骤: attach/detach 的全部世界写——ChildOf/ChildrenBuffer/AttachedLocalPose 增删、子实体 WorldPositionCm/PreviousWorldPositionCm/FacingDirection 写、写权 pending（arbiter 待结算切换）。失败回滚 = 恢复挂接前组件状态 + 撤销 arbiter pending。

## 6. Config SSOT

行为配置落在: effect template / graph / catalog（路径）: `mods/*/assets/GAS/effects.json` 的 `relation` 块（`operation: Attach/Detach` + `localPose`/`detachPlacement` 参数）；预置组合落在 `Entities/templates.json` 的 `children` 块。

是否新增 JSON schema: **YES（既有 schema 的字段扩展，非新 DSL）** — 若 YES 说明为何不通过组合表达: localPose/detachPlacement 是原子 op 的**参数**（数据），不是行为开关；新变体（不同 offset/落位半径/朝向继承组合）只改 JSON 参数与 effect 链接线，不改 Core enum。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum（`DetachPlacement` 是 op 参数 enum，先例 `DisplacementDirectionMode`；`AttachedOffsetRotation` 是组件数据 enum）
- [x] 未新建与 spawn 平行的物化管线（children 走 `RuntimeEntitySpawnQueue`；map 装载复用 `EntityBuilder` 同一物化路径）
- [x] 未把 placement 校验塞进 lifecycle op（attach 的环检测/容量是结构不变量 fail-fast，不是地形 placement 校验）
- [x] 未添加「说不清的」默认 fallback（detach 默认 `KeepWorldPose` 是显式声明的落位策略参数默认值，文档化）
- [x] 未在 RelationshipRuntime 新增 `AttachedTo` 平行类型（只扩展 ChildOf/ChildrenBuffer 组件边）

## 8. Next variant test

「下一个 Mod 变体」（例：降落伞空投 detach 落到扇形区、旗帜挂到炮管末端、多炮塔不同 offset）将修改: **graph 连线 / effect 步骤 / JSON 参数**（Core enum 不动；`DetachPlacement` 若出现第三种落位策略才扩 op 参数枚举——与 `DisplacementDirectionMode` 增加方向的同级别扩展，属 op 参数演进而非 preset 开关）。

## 9. 票内决策记录（票面要求拍板项）

### 9.1 写权决策：引入 `PoseAuthorityKind.Attached`

- 架构事实核验：`PoseAuthorityKind` 未被任何架构测试钉死；全部读点为 `DisplacementRuntimeSystem`（只比对 Displacement）与 `MassNavigationPoseAuthorityBridge`（显式 throw 未支持转移——本票扩展 Nav↔Attached 两条转移，复用 displaced 求解器状态：跳过积分、邻居避让、每 sync 节拍回灌已提交位姿，语义与位移窗口一致且已被 `SyncDisplacedAgentPoses` 支持）。
- 结论：**采用 Attached=4**，经 `PoseAuthorityArbiter` pending transition 在固定步边界结算（保持"切换只在固定步边界经 CommandBuffer 生效"合同）；attach 授予（Nav→Attached）、detach 归还（Attached→Nav），事务可撤销 pending（回滚调 arbiter 移除待结算切换）。
- 附带规则：子实体无 `PoseAuthority`（未声明 MovementParticipation）时无写者冲突，attach 不授予任何东西（sink 即唯一写者）；`Physics`/`Displacement` 持有者 attach fail-fast；无 `PoseAuthority` 的模板 children 禁止声明 MovementParticipation（spawn 期 fail-fast）。

### 9.2 parent-moved 门的保守边界

门规则：父 `WorldPositionCm.Current == Previous` 且子**不依赖父朝向**（offset 旋转源非父/子朝向、不继承朝向）时跳过该子（静态父样例整树跳过）。依赖朝向的子（炮塔 offset 随底盘朝向旋转）无法从位姿差推断朝向变化（无 PreviousFacing），不适用门、每帧重算——正确性优先，多做的功是常数。票面"父 Current==Previous 时整树跳过"在位置依赖子树上完整成立。

### 9.3 容量与槽位模型

`ChildrenBuffer` 保持 16 硬顶 fail-fast（超容抛 `ChildrenCapacityExceeded`，含容量数字）——"解决"指超容从静默丢边变为显式合同错误 + 环检测防病态深链。槽位模型：detach 周界落位的槽序 = 子在父 ChildrenBuffer 快照中的序（同批多 detach 天然错开，RTS 先例同构）。

### 9.4 manifestation 迁移

`ManifestationMotion2DSystem` 保留朝向职责（sweep/parent execution target），位置跟随职责并入通用 sink：`followParentPosition` 的 manifestation 在组件装配时自动获得 `AttachedLocalPose`（offset = ForwardOffsetCm 沿自身朝向旋转，`AttachedOffsetRotation.OwnFacing`）。朝向与位姿因此分离一步（sink 在 PostMovement、朝向在 EffectProcessing），对齐票面"effect 驱动的父级位移维持一帧滞后语义"。

### 9.5 map 装载与 runtime spawn 的双路径说明

runtime 模板 spawn：children 经 `RuntimeEntitySpawnQueue` enqueue（票面指定路径）；map 装载：`MapLoader` 非 batch 路径同步递归物化 children（同一 `EntityBuilder` 物化 + 同一位姿组合数学，仅时序不同——map 装载本就是同步 lane）。两路径共享 `AttachedPoseMath` 组合函数，无平行物化管线。

## 10. 审计后修正（对抗性审计 + 约定修正，2026-08-23）

### 10.1 HIGH-1：parent-moved 门删除，sink 恒重算

审计发现（已逐环核实）：`SavePreviousWorldPositionSystem`（SchemaUpdate 步首）先把 Previous=Current 抹平，而真实位姿写者大量存在于 sink 之后（PostMovement 后段的 `MassNavigationSimulationStepSystem`、AbilityActivation 的 `MoveToWorldCmOrderSystem`、EffectProcessing 的位移/投射物）——sink 运行时父的 `Current==Previous` 是常态，Previous 比对门会让位置依赖子（炮塔/骑乘/驻防形态）在真实 nav 驱动父下**永久冻结**，且被两层测试时序差异掩护（harness 把求解器排在 sink 前 + 不跑 SavePrevious；验收用 InputCollection 脚本写者并注释"与真实 nav 写者同位次"——不实）。

修正：门整体删除（`LastGateSkippedCount`/`DependsOnFacing` 一并移除），sink 恒重算——compose 只是几次 Fix64 运算，恒重算不构成热点；静态父重派生幂等。**这是对票面"parent-moved 门（静态父整树跳过）"的显式偏离**，理由：门的推断前提（Previous 比对可识别"未移动"）在引擎真实时序下不成立。回归测试 `PostSinkWriterTiming_PositionDependentChild_StillFollows` 钉住该时序。两个 harness 同步改为引擎真实时序（sink 先于求解器；验收测试注释如实标注 pre-sink 写者与 post-sink 用例的覆盖分工）。

### 10.2 约定修正：挂接链唯一 mass nav——成员身份挂起/恢复取代 displaced 复用

按引擎约定修正（此前实现违反）：**attachment 挂接链上只允许一个 mass nav 成员**（独立移动的根）。原实现保留子实体 nav agent 身份、复用位移窗口的 displaced 求解器状态（跳积分/避让/回灌）——删除：

- `MassNavigationPoseAuthorityBridge` 的 Nav↔Attached 两条转移回退为 main 形态；对 Attached 转移显式 no-op（成员身份 rebuild 已维护求解器状态：挂起回收槽位、恢复按已提交位姿重播种）。
- nav 域新增 `SuspendedNavMembership` 快照组件与 `MassNavigationMembership.Suspend/Restore` 单点：attach 摘除 `MassNavigationAgent/AgentIndex/AgentProfile` 三组件并存快照；detach / 孤儿自愈回放 Agent 标记（旧 Index 不复用，绑定系统按已提交位姿重播种）。成员数变化由既有 `MassNavigationAuthoredAgentBindingSystem` 感知（rebuild/append）。
- 事务路径同构：`StageAttach/StageDetach` stage 成员身份挂起/恢复（同事务正反抵消），提交期经 `_structuralCommands` 落地；"nav agent 无 MovementParticipation attach fail-fast"旧合同删除（成员身份挂起后求解器不再是竞争写者，双写前提消失）。
- `EntityAttachmentTests` 旧钉子测试（fail-fast 合同）改写为新约定合同；`MassNavigationAttachedAuthorityTests` 重写为成员身份合同（attach 后 TotalAgents==1、sink 镜像载具已提交位姿、detach 重播种恢复行军）。

### 10.3 MEDIUM 修复

- M1：`StageAttach` 组合初始位姿的 facing 改走 staged 视图（新增 `TryReadRelationFacing`，与位置同一纪律）——同事务"先改父朝向再挂子"不再用陈旧世界态。
- M2：OwnFacing 偏移的朝向回退链恢复为迁移前 manifestation 语义（子 facing → 父 facing → 0，单点 `AttachedPoseMath.ResolveOwnFacingRad`，sink/直接路径/事务/spawn children 四处统一）。

### 10.4 验证（修正后）

- `Ludots.Core` 构建 0 错误。
- GasTests attachment 全集 23/23 通过（含 HIGH-1 回归、成员身份合同、事务回滚）。
- `MassNavigationAttachedAuthorityTests` 1/1 通过（引擎时序 + 成员身份 + 重播种行军）。
- ArchitectureTests 见票面评论（基线 4 失败与本票无关）。
