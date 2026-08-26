# Entity Attachment（实体挂接）

## 1. 概述

Entity Attachment 只负责一件事：把子实体绑到父实体上，并按局部位姿持续派生世界坐标。它不拥有导航、物理、Trigger 业务，也不替作者自动协调多个位置写者。

正式入口：

- 绑定触发：`RelationOperation.Attach` / `Detach`（GAS Effect / `AttachmentOps`）
- 位置消费：`AttachmentPositionSyncSystem`（PostMovement，父先子后）
- Authoring：`EntityTemplate.children` + `AttachedPoseMath`（与 MapLoader / Runtime spawn 共用）

ADR / 计划 SSOT：GitHub `#239` / `#244`；任务票 `#1064`。本页是 attachment 运行时合同的文档正本；Presenter 骨骼挂载见 `presenter-transform-and-attachment.md`，实体域 Trigger 作用域见 `entity-trigger-graph-subworld.md`。

## 2. 结构

```text
Effect / AttachmentOps
  └─ ChildOf + ChildrenBuffer + AttachedLocalPose
  └─ PoseAuthority Nav ↔ Attached（固定步边界结算）
        │
        ▼
AttachmentPositionSyncSystem
  └─ 父 WorldPosition/Facing ∘ LocalPose → 子 WorldPosition/Previous
```

组件边只用 `ChildOf` / `ChildrenBuffer`，禁止平行 `AttachedTo` Relationship 类型。世界坐标仍是唯一仿真真相；`AttachedLocalPose` 不存派生结果。

## 3. 详情

### 3.1 职责闭合

| 负责 | 不负责 |
|------|--------|
| 父子关系、环/容量校验 | Mass Navigation 求解、避让、重播种策略（作者自决） |
| 局部位姿与同步 | Trigger 图编排、事件总线 |
| `PoseAuthority` Attached 授予/归还 | Physics / Displacement 窗口内的自动协调 |
| 孤儿拆边与写权归还 | 选中、指挥、视野、属性转移 |

### 3.2 关系

- 一子至多一父；`ChildOf` 与父 `ChildrenBuffer` 必须双向一致。
- 环检测覆盖世界边与事务 staged 边；超 `MAX_CHILDREN_BUFFER_CAPACITY` 立即失败。
- 父死亡：普通子走 orphan cleanup；带自管生命周期标记的子由生命周期系统处置。
- 周界 detach 槽位来自 ChildrenBuffer 快照序号，禁止猜槽。

### 3.3 位姿

- 深度序父先子后；`None` / `ParentFacing` / `OwnFacing` 语义固定。
- `inheritParentFacing=true` 与 `OwnFacing` 互斥（装载期失败）。
- 缺必要父/子位姿组件失败，不自动补组件。
- 不做 `Current==Previous` 门控；恒重算以保证 post-sink 写者时序下子实体不冻结。

### 3.4 写权与事务

- Attach：`Nav → Attached`；Detach：`Attached → Nav`；只在固定步边界经 `PoseAuthorityArbiter` 结算。
- `Physics` / `Displacement` 持有者 attach fail-fast。
- 事务路径：`EffectPhaseSideEffectTransaction.StageAttach/StageDetach`；失败回滚 ChildOf、ChildrenBuffer、AttachedLocalPose、世界位姿、Facing、pending。
- 同事务 Attach→Detach 正反抵消；已有 pending 不被覆盖。

### 3.5 Spawn / Template

- `EntityTemplate.children` 复用正式 spawn / MapLoader 物化，共用 `AttachedPoseMath`。
- 模板子禁止声明自由移动（`MovementParticipation`）；自由移动单位必须经 AttachOp。
- 装载期预演引用、环、子数；不生成第二套物化管线。

### 3.6 与 TriggerGraph

- TriggerGraph **不得**直接调用 `AttachmentOps` 改 World。
- TriggerGraph 方言**故意排除** `ApplyEffectTemplate`（effect 事务 op 留在 Effect 图）。
- 玩家“上车/下车”走 Ability / Effect 正式入口；实体域 Trigger 只按 `ChildOf` 树判定事件归属。
- Attachment×Trigger 闭环验收 = Effect 路径真实关系/位姿断言 + 实体域 attached descendant 路由，而不是在 Trigger 图里再写一套挂接逻辑。

### 3.7 当前实现备注（审计 2026-08-26）

代码与 `artifacts/gas-composition-gate-entity-attachment.md` §10.2 仍对挂接子执行 `MassNavigationMembership.Suspend/Restore`，并快照导航组件。这与本页 §3.1「不负责导航成员身份」及票面「作者自决是否继续参与导航」冲突，记为 **HIGH 未决决策**：不得在本页静默改写成“已收口”，须在 `#1064` / `#244` 拍板后二选一：

1. 删除 attachment 内的 nav 挂起/恢复，导航身份完全由作者配置与独立系统负责；或  
2. 正式把「挂接链唯一 mass nav 成员」写入本页与 ADR，并接受 Mass Nav 识别 Attached 转移。

## 4. 场景

1. 单层炮塔挂接与拆除。  
2. 三层底盘→炮塔→炮管。  
3. 父移动、转向、静止；静态父恒重算幂等。  
4. GAS Attach/Detach 骑乘上车与周界下车。  
5. 事务中途失败全量回滚。  
6. 父销毁后孤儿清理。  
7. 模板 children 预置挂接。  
8. 实体域根图接收挂接子事件、拒绝外部实体。

## 5. 边界

- 不做选中/指挥 suppression。  
- 不做平行 Relationship `AttachedTo`。  
- 不把 Trigger S4/S5、AgentBridge 实机验收塞进 attachment 修复。  
- 不把 Mass Navigation 系统顺序调整塞进 attachment（除非 ADR 采纳方案 2）。  
- 热路径不扩容、不 LINQ、不改实体结构飞线；scratch 超容显式失败。

## 6. UAT

```gherkin
Feature: 玩家通过效果触发实体挂接

  Scenario: 触发上车
    Given 玩家看到载具和骑乘单位
    When 玩家触发上车效果
    Then 骑乘单位应拥有载具父实体
    And 骑乘单位应保持声明的局部偏移
    And 其它导航或移动成员身份的后续结果应遵循作者配置与已拍板合同

  Scenario: 触发下车
    Given 骑乘单位已经挂接到载具
    When 玩家触发周边散布下车效果
    Then 父子关系应被解除
    And 骑乘单位应落在稳定槽位
    And 后续位置写入结果应遵循作者配置

  Scenario: 触发失败
    Given 目标父实体已经达到子实体容量上限
    When 玩家再次触发上车效果
    Then 系统应明确报告容量错误
    And 父子关系、位姿和写权都不得发生部分改变

  Scenario: 实体域图接收挂接子实体事件
    Given 根实体声明了实体域 TriggerGraph
    And 一个子实体已经挂接到根实体
    When 子实体产生声明的事件
    Then 根实体图应收到该事件
    And 外部实体事件不得改变根实体状态
```

证据锚：

- 单元/事务：`EntityAttachmentTests`、`AttachmentPositionSyncSystemTests`
- Capability：`EntityAttachmentCapabilityAcceptanceTests` + `artifacts/acceptance/entity-attachment/battle-report.md`
- 实体域 Trigger：`TriggerGraphEntityDomainTests`
- 审计矩阵：`artifacts/audits/entity-attachment-closeout-audit.md`
