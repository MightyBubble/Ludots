# Entity Attachment 收口审计（2026-08-26）

## 1. 概述

目标不是证明“代码能跑”，而是证明职责闭合、失败一致、边界可证伪。

SSOT：

- `gitbook/architecture/entity-attachment.md`（本轮补正本）
- Issue `#1064`（OPEN）/ `#239` / `#244`
- `EntityAttachmentTests` / `AttachmentPositionSyncSystemTests` / `EntityAttachmentCapabilityAcceptanceTests`
- Trigger 进度入口：`gitbook/architecture/graph-capability-status.md`
- 组合门：`artifacts/gas-composition-gate-entity-attachment.md`

总判：

| 面 | 结论 |
|----|------|
| Attachment Core（关系/位姿/写权/事务/spawn） | **基本收口**，有代码+测试+capability 证据 |
| Attachment×Mass Navigation 职责 | **HIGH 未决**：代码挂起/恢复 nav 成员，与票面“只管绑定与位置 / 作者自决”冲突 |
| Attachment×Trigger | **P0 缺口已澄清**：Trigger 方言禁止 `ApplyEffectTemplate`；正式上车走 Effect/GAS；实体域只消费 `ChildOf` 树 |
| Trigger 总体 S4/S5 | **仍开着**，不得塞回 Attachment |

## 2. 结构（审计矩阵）

| 节 | 项 | 状态 | 证据 |
|----|----|------|------|
| A | 只处理 ChildOf/ChildrenBuffer/AttachedLocalPose/同步/PoseAuthority | ⚠ 部分 | `AttachmentOps`/`AttachmentPositionSyncSystem`；但另写 nav Suspend/Restore |
| A | 不读/写/快照导航组件 | ❌ | `AttachmentStateSnapshot` 快照 Agent/Index/Profile/SuspendedNav |
| A | 不调用 Mass Navigation API | ❌ | `MassNavigationMembership.Suspend/Restore` |
| A | Mass Nav 不识别 Attachment | ⚠ | Bridge 对 Attached 转移 no-op，但仍知 Attached |
| A | Trigger 不直接改父子关系 | ✅ | MapTriggers 无 `AttachmentOps` 调用 |
| A | 多写者不自动协调 | ⚠ | PoseAuthority 冲突 fail-fast；但 nav 成员被自动挂起 |
| B | 一子一父 / 双向一致 / 环 / 容量 | ✅ | `RelationOps` + `EntityAttachmentTests` |
| B | 孤儿 / 异常状态失败关闭 | ✅ | sink orphan + 事务校验 |
| B | 周界不猜槽 | ✅ | ChildrenBuffer 快照序号 / 事务位图 |
| C | 拓扑序 / 三旋转源 / 无 Current==Previous 门 | ✅ | sink 恒重算；gate 文档 §10.1 |
| C | inherit+OwnFacing 冲突 | ✅（本轮补齐 effect 装载） | authoring 已有；`EffectTemplateLoader` 本轮对称 |
| C | 缺组件失败、不自动补 | ✅ | sync/ops fail-fast |
| D | Nav↔Attached 边界结算 / 事务回滚 | ✅ | `EntityAttachmentTests` |
| D | Physics/Displacement 冲突 | ✅ | `AuthorityConflictError` |
| D | Attach→Detach 抵消 / 外部改写失败 | ✅ | 同文件用例 |
| E | template children 同管线 / 禁自由移动 | ✅ | MapLoader + RuntimeEntitySpawn |
| F | 热路径预分配 / 超容失败 | ⚠ | `ScratchCapacity=8192` **硬编码**；无专项 perf 套件 |
| G | Trigger 只经 GAS 触发挂接 | ⚠ | 方言禁止 Trigger 内 ApplyEffect；玩家路径靠 Effect 队列 |
| G | 实体域 attached descendant | ✅ | `TriggerGraphEntityDomainTests`（仍用直接 Attach 建树） |
| G | Effect→真实关系/位姿 | ✅（本轮加事务路径钉子） | capability 验收 + 新 `GasEffectPath_*` |

## 3. 详情

### A. 职责边界

- `#1064` 正文：「attachment 只管绑定与位置」。
- 实现与组合门 §10.2 引入「挂接链唯一 mass nav」：attach 挂起成员、detach/孤儿恢复并重播种。
- Mass Nav 侧 `MassNavigationPoseAuthorityBridge` 显式认识 `PoseAuthorityKind.Attached`。
- **判决**：相对审计清单 A 为失败；相对当前落地约定为“有意偏离”。必须在 `#1064`/`#244` 二选一拍板（见 `entity-attachment.md` §3.7），禁止再口头说“已收口”。

### B. 关系正确性

覆盖充分：环（世界+staged）、容量硬顶、缺失 ChildOf/ChildrenBuffer、父死 orphan、周界活父要求。未发现静默丢边。

### C. 位姿正确性

三层坦克 / 静态聚落 / 原地转向 / OwnFacing / 独立炮塔朝向 / post-sink 写者回归均有测试。parent-moved 门已删除（正确性优先）。

缺口：`EffectTemplateLoader` 原先未校验 inherit+OwnFacing（模板 authoring 有）。**本轮已补。**

### D. 写权与事务

事务路径与直接路径语义对齐；回滚覆盖关系、局部位姿、世界位姿、Facing、pending、（现行）nav 快照。结构命令容量走既有 `CapacityExceededError`。

### E. Spawn / Map / Template

`AttachedPoseMath` 单点；MapLoader / Runtime spawn 共用；模板子禁止 `MovementParticipation`；预演环与引用。无第二物化管线。

### F. 性能与 ECS

sink 使用预分配数组 + chunk span；超 `ScratchCapacity` 抛错。缺口：容量来自常量非配置；缺少稳定/深层/大量子/孤儿清理的专项性能测试。

### G. Trigger 集成

- Effect 装载严格要求 Attach 的 parent+localPose、Detach 的 detachPlacement。
- TriggerGraph **作者态不能** `ApplyEffectTemplate`（`TriggerGraphAuthoringTests` + 本轮 `TriggerGraphDialect_*`）。
- 因此用户文案中的 `TriggerGraph → ApplyEffectTemplate → Attach` **不是现行合法作者路径**；合法路径是 Ability/Effect 请求队列 → `HandleApplyRelation` → `StageAttach`。
- 实体域测试用 `AttachmentOps.Attach` 建树只验证路由，不验证 Effect 闭环——Effect 闭环由 capability + 本轮 `GasEffectPath_*` 承担。
- Trigger S4 时序全文、S5 AgentBridge 实机：见 `graph-capability-status.md` / `artifacts/acceptance/trigger-entity-subworld/trigger-domain-expansion.md`（AgentBridge 未执行）。**不属于 Attachment 修复范围。**

## 4. 场景覆盖（16 + Cucumber）

| # | 场景 | 覆盖 |
|---|------|------|
| 1 | 单层炮塔 | ✅ sync + capability |
| 2 | 三层底盘→炮塔→炮管 | ✅ |
| 3 | 父移动/转向/静止 | ✅ |
| 4 | 子保留独立写者→作者承担 | ⚠ PoseAuthority 冲突 fail-fast；nav 自动挂起偏离“作者承担” |
| 5 | Attach 事务中途失败 | ✅ |
| 6 | Detach 周边槽位耗尽 | ⚠ 错误码存在；专项强迫用例弱 |
| 7 | 父销毁孤儿清理 | ✅ |
| 8 | 同事务 Attach→Detach | ✅ |
| 9 | 多实体同时挂接 | ✅（双 detach 槽位） |
| 10 | 深层关系树 | ⚠ 有深度序；无压力容量边界套件 |
| 11 | 模板 children | ✅ capability 预置 |
| 12/13 | Trigger 触发 Attach/Detach | ❌ 方言挡住；应由 Effect/Ability 场景冒充“玩家触发” |
| 14 | EntityDied 清理 | ✅ 实体域 died entry |
| 15 | Map unload 不重复 EntityDied | ⚠ 有生命周期测试；专项 unload 竞态需对照 S4 |
| 16 | attached descendant 事件归属 | ✅ |

Cucumber 上车/下车/失败/实体域：capability + 事务/容量测试 + 实体域测试可映射；“导航身份不被系统自动修改”在现行实现下**不成立**（见 A）。

## 5. 边界（不要混进本线）

- Trigger S4/S5、图 ID 整数、事件丢弃计数、字符串寄存器、Graph 分层——只报告，不在 Attachment PR 修。
- 不得为通过审计清单 A 而在未拍板前偷偷删 nav Suspend（那是 Core 合同变更）。

## 6. UAT / 本轮动作

已做：

1. 补 SSOT `gitbook/architecture/entity-attachment.md` 并挂 SUMMARY。  
2. `EffectTemplateLoader` 对称校验 inherit+OwnFacing。  
3. 测试：`Load_AttachLocalPose_InheritFacingWithOwnFacing_IsRejected`、`GasEffectPath_AttachThenDetach_CommitsRealRelationPoseAndAuthority`；方言钉子沿用 `TriggerGraphAuthoringTests.DescriptorProjections_TriggerGraphMirrorsScriptIncludingYield`。  
4. 本审计文件。

待拍板 / 后续票：

- HIGH：nav 成员挂起合同 vs 作者自决。  
- MEDIUM：`ScratchCapacity` 配置化；attachment 性能测试套件；Detach 槽位耗尽强迫用例。  
- P0（Trigger 域，非 Attachment Core）：S4 时序对齐、S5 实机；若产品要“图内直接上车”，需单独 ADR 开放受限 effect 入队 op，而不是让 Trigger 调 `AttachmentOps`。
