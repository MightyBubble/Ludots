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
| Attachment×Mass Navigation 职责 | **HIGH 未决（审计 FAIL）**：代码挂起/恢复 nav 成员，与票面“只管绑定与位置 / 作者自决”冲突；须 #1064/#244 拍板，本审计不偷偷改合同 |
| Attachment×Trigger | **P0 已澄清并补证**：Trigger 方言禁止 `ApplyEffectTemplate`；正式上车走 Effect/GAS；实体域只消费 `ChildOf` 树 |
| Trigger 总体 S4/S5 | **仍开着**，不得塞回 Attachment |

## 2. 结构（审计矩阵）

| 节 | 项 | 状态 | 证据 |
|----|----|------|------|
| A | 只处理 ChildOf/ChildrenBuffer/AttachedLocalPose/同步/PoseAuthority | ⚠ 部分 | `AttachmentOps`/`AttachmentPositionSyncSystem`；但另写 nav Suspend/Restore |
| A | 不读/写/快照导航组件 | ❌ | `AttachmentStateSnapshot` 快照 Agent/Index/Profile/SuspendedNav |
| A | 不调用 Mass Navigation API | ❌ | `MassNavigationMembership.Suspend/Restore` |
| A | Mass Nav 不识别 Attachment | ⚠ | Bridge 对 Attached 转移 no-op，但仍知 Attached |
| A | Trigger 不直接改父子关系 | ✅ | `MapTriggersSources_DoNotCallAttachmentOps` |
| A | 多写者不自动协调 | ⚠ | PoseAuthority 冲突 fail-fast；但 nav 成员被自动挂起 |
| B | 一子一父 / 双向一致 / 环 / 容量 | ✅ | `RelationOps` + `EntityAttachmentTests` |
| B | 孤儿 / 异常状态失败关闭 | ✅ | sink orphan + 事务校验 |
| B | 周界不猜槽 | ✅ | ChildrenBuffer 快照序号 / 事务位图 |
| C | 拓扑序 / 三旋转源 / 无 Current==Previous 门 | ✅ | sink 恒重算；`DeepAttachmentTree_*` |
| C | inherit+OwnFacing 冲突 | ✅ | authoring + `EffectTemplateLoader` + loader 测试 |
| C | 缺组件失败、不自动补 | ✅ | sync/ops fail-fast |
| D | Nav↔Attached 边界结算 / 事务回滚 | ✅ | `EntityAttachmentTests` |
| D | Physics/Displacement 冲突 | ✅ | `AuthorityConflictError` |
| D | Attach→Detach 抵消 / 外部改写失败 | ✅ | 同文件用例 |
| D | 周界槽位耗尽 | ✅ | `Detach_WhenPerimeterSlotsExhaustedInSameTransaction_FailsClosed` |
| E | template children 同管线 / 禁自由移动 | ✅ | MapLoader + RuntimeEntitySpawn |
| F | 热路径预分配 / 超容失败 | ✅ | `gasRuntimeCapacity.attachmentPositionSyncScratchCapacity` + `ScratchCapacityExceeded_*` |
| F | 专项压测套件（稳定/深层/大量/孤儿） | ⚠ | 有深度序与超容失败；无独立 perf 基准 harness（记 MEDIUM 后续票） |
| G | Trigger 只经 GAS 触发挂接 | ✅ 澄清 | 方言禁止 Trigger 内 ApplyEffect；玩家路径靠 Effect 队列 |
| G | 实体域 attached descendant | ✅ | `TriggerGraphEntityDomainTests` |
| G | Effect→真实关系/位姿 | ✅ | capability + `GasEffectPath_*` |

## 3. 详情

### A. 职责边界

- `#1064` 正文：「attachment 只管绑定与位置」。
- 实现与组合门 §10.2 引入「挂接链唯一 mass nav」：attach 挂起成员、detach/孤儿恢复并重播种。
- Mass Nav 侧 `MassNavigationPoseAuthorityBridge` 显式认识 `PoseAuthorityKind.Attached`。
- **判决**：相对审计清单 A 为失败；相对当前落地约定为“有意偏离”。必须在 `#1064`/`#244` 二选一拍板（见 `entity-attachment.md` §3.7），禁止再口头说“已收口”。

### B–E

关系、位姿、写权/事务、spawn/template 覆盖充分；本轮补周界槽位耗尽失败关闭与 Effect 装载朝向互斥。

### F. 性能与 ECS

本轮将 sink scratch 改为 `GasRuntimeCapacity.AttachmentPositionSyncScratchCapacity`（默认 8192，缺省 JSON 仍合法），引擎装配注入；超容显式失败有测试。未新增独立 benchmark harness——记 MEDIUM，不阻塞审计收口。

### G. Trigger 集成

- Effect 装载严格要求 Attach 的 parent+localPose、Detach 的 detachPlacement。
- TriggerGraph **作者态不能** `ApplyEffectTemplate`（`TriggerGraphAuthoringTests`）。
- 因此 `TriggerGraph → ApplyEffectTemplate → Attach` **不是现行合法作者路径**；合法路径是 Ability/Effect 请求队列 → `HandleApplyRelation` → `StageAttach`。
- Trigger S4/S5：见 `graph-capability-status.md`（AgentBridge 未执行）。**不属于 Attachment 修复范围。**

## 4. 场景覆盖（16 + Cucumber）

| # | 场景 | 覆盖 |
|---|------|------|
| 1 | 单层炮塔 | ✅ sync + capability |
| 2 | 三层底盘→炮塔→炮管 | ✅ |
| 3 | 父移动/转向/静止 | ✅ |
| 4 | 子保留独立写者→作者承担 | ⚠ PoseAuthority 冲突 fail-fast；nav 自动挂起偏离“作者承担” |
| 5 | Attach 事务中途失败 | ✅ |
| 6 | Detach 周边槽位耗尽 | ✅ 本轮 |
| 7 | 父销毁孤儿清理 | ✅ |
| 8 | 同事务 Attach→Detach | ✅ |
| 9 | 多实体同时挂接 | ✅ |
| 10 | 深层关系树 | ✅ 深度序 + 超容失败 |
| 11 | 模板 children | ✅ capability 预置 |
| 12/13 | Trigger 触发 Attach/Detach | ✅ 澄清为方言禁止；玩家路径由 Effect/GAS 覆盖 |
| 14 | EntityDied 清理 | ✅ 实体域 died entry |
| 15 | Map unload 不重复 EntityDied | ⚠ 有生命周期测试；专项 unload 竞态属 Trigger S4 |
| 16 | attached descendant 事件归属 | ✅ |

## 5. 边界（不要混进本线）

- Trigger S4/S5、图 ID 整数、事件丢弃计数、字符串寄存器、Graph 分层——只报告，不在 Attachment PR 修。
- 不得为通过审计清单 A 而在未拍板前偷偷删 nav Suspend（那是 Core 合同变更）。

## 6. UAT / 本轮动作

已做：

1. 补 SSOT `gitbook/architecture/entity-attachment.md` 并挂 SUMMARY。  
2. `EffectTemplateLoader` 对称校验 inherit+OwnFacing。  
3. sink scratch 配置化并接线 `GameEngine` / `assets/game.json`。  
4. 测试：朝向互斥装载、`GasEffectPath_*`、周界槽位耗尽、scratch 超容、深层树、MapTriggers 不调 `AttachmentOps`、容量配置校验。  
5. 本审计文件与 `artifacts/acceptance/entity-attachment-audit/` 运行证据。

待拍板 / 后续票（不阻塞本审计交付）：

- HIGH：nav 成员挂起合同 vs 作者自决（#1064/#244）。  
- MEDIUM：attachment 独立 perf harness；Map unload×EntityDied 竞态归 Trigger S4。  
- 若产品要“图内直接上车”，需单独 ADR 开放受限 effect 入队 op，而不是让 Trigger 调 `AttachmentOps`。

## 7. 完成度核对（相对目标）

| 目标要求 | 现证 | 结果 |
|----------|------|------|
| 可证伪审计结论（A–G） | 本文 §2–§5 + SSOT | ✅ |
| 必要补齐：P0 Effect 真实关系/位姿 | `GasEffectPath_*` + capability | ✅ |
| 必要补齐：装载互斥 / 槽位耗尽 / scratch 配置 / Trigger 不写边 | 对应测试 + 配置字段 | ✅ |
| HIGH nav 职责冲突 | 明确 FAIL + 拍板入口，未静默改合同 | ✅（审计完成；实现待 ADR） |
| Trigger S4/S5 实机 | 排除出 Attachment 范围并指向进度页 | ✅（非本目标实现项） |
