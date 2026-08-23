# 实体挂接与 Pose Authority

本文是 Core 实体父子挂接的正式合同。它覆盖结构、局部位姿、写权、MassNavigation 成员身份和失败恢复；不覆盖 Presenter 骨骼挂载。

## 1. 概述

实体挂接由 `AttachmentOps` 原子操作和 `AttachmentPositionSyncSystem` 同步系统组成：

- `ChildOf` 表示子实体当前挂在哪个父实体下。
- 父实体的 `ChildrenBuffer` 保存子实体集合。
- `AttachedLocalPose` 保存相对父锚点的局部位姿。
- `WorldPositionCm` 是最终世界位姿的仿真真相；同步系统每固定步从父位姿和局部位姿重新派生。
- `PoseAuthority.Attached` 保证挂接同步器是唯一位姿写者。

## 2. 结构

```text
AttachmentOps.Attach / Detach / DetachToPerimeter
├─ 预检：存活、父位姿、环、容量、写权、位姿参数
├─ 事务快照：父子结构、位姿、写权相关组件、导航成员快照
├─ RelationOps：ChildOf + ChildrenBuffer
├─ MassNavigationMembership.Suspend / Restore
└─ PoseAuthorityArbiter：固定步边界结算 Attached ↔ Nav

AttachmentPositionSyncSystem
└─ PostMovement：父先子后、恒重算、写 WorldPositionCm + PreviousWorldPositionCm
```

## 3. 详情

### 3.1 Attach

`Attach` 必须先验证目标实体、环检测、父实体世界位姿、局部位姿、写权仲裁器和父子容量。执行阶段按以下顺序组合：

1. 挂起子实体的 MassNavigation 成员身份并保留完整快照。
2. 申请 `PoseAuthority.Attached` 写权切换。
3. 通过 `RelationOps.SetParent` 写入父子结构。
4. 用 `AttachedLocalPose` 计算初始世界位姿。

任一步骤失败，都恢复快照并取消未结算的写权待办；不会留下只有 `ChildOf`、只有挂起成员、或只有 Attached 写权的半绑定状态。

### 3.2 位姿同步

`AttachmentPositionSyncSystem` 位于 `PostMovement`，在网格同步前运行。它使用父先子后的深度顺序处理多层挂接，并且每次固定步恒重算：

```text
parent WorldPositionCm
  + AttachedLocalPose.OffsetCm（按 None / ParentFacing / OwnFacing 旋转）
  -> child WorldPositionCm
  -> child PreviousWorldPositionCm
```

系统只允许 `PoseAuthority.Attached` 或没有写权组件的子实体进入写位姿路径。检测到其他写权时必须 fail-fast。

### 3.3 Detach

- `KeepWorldPose`：解除父子结构，保留当前已提交世界位姿。
- `ParentPerimeterRing`：根据父 `ChildrenBuffer` 快照中的槽位，把子实体放到父实体周界环上；批量解除时每个子实体使用不同槽位。
- 解除挂接会恢复挂起的 MassNavigation 成员声明，但不复用旧 `MassNavigationAgentIndex`；绑定系统按当前已提交位姿重新播种。
- `PoseAuthority.Attached` 的归还在固定步边界结算，避免同一固定步出现双写者。

### 3.4 父实体消失

父实体死亡或失去有效位姿时，同步系统清理普通孤儿的 `ChildOf` 和 `AttachedLocalPose`，并排队归还 Attached 写权。带自管生命周期标记的表现实体由其生命周期系统处理，不由同步器抢拆。

## 4. 场景

- 载具移动时，乘客保持局部偏移，玩家看到乘客稳定跟随载具而不是继续被导航目标拉走。
- 乘客下车后落在载具周界的不同槽位，恢复导航成员身份并从当前落点继续行军。
- 父实体被销毁时，普通子实体解除挂接并恢复可移动写权；有自管生命周期的表现实体按自己的生命周期结束。
- 父位姿、容量、写权或成员快照不满足要求时，玩家看到明确的失败结果，世界中不存在半挂接对象。

## 5. 边界

- 挂接不是 Presenter 骨骼 attachment；Presenter 变换合同仍在 `gitbook/architecture/presenter-transform-and-attachment.md`，两者不共享状态。
- 挂接不负责导航求解、槽位分配或目标规划；它只负责成员身份的挂起/恢复和已提交位姿交接。
- `AttachedLocalPose` 不保存派生世界坐标；世界坐标只写入 `WorldPositionCm`。
- 不允许用 parent-moved 门跳过同步；挂接同步点之后仍可能有其他正式位姿写者，因此采用恒重算并依赖写权合同避免冲突。

## 6. UAT

```gherkin
Feature: 实体挂接与解除挂接

  Scenario: 乘客跟随载具移动
    Given 玩家让乘客成为载具的子实体
    When 载具沿导航目标移动
    Then 乘客保持声明的局部偏移并跟随载具
    And 乘客不会继续占用独立导航成员槽位

  Scenario: 乘客解除挂接后恢复行军
    Given 乘客正在跟随载具
    When 玩家让乘客解除挂接并选择周界落位
    Then 乘客落在载具周界的确定槽位
    And 乘客恢复导航成员身份
    And 乘客从当前落点继续移动

  Scenario: 挂接失败不留下半绑定状态
    Given 父实体缺少有效位姿或写权仲裁器不可用
    When 系统尝试挂接乘客
    Then 操作明确失败
    And 乘客仍保留原来的父子、位姿和导航成员状态
```

自动化证据：

- `src/Tests/GasTests/Effect/EntityAttachmentTests.cs`
- `src/Tests/GasTests/Effect/AttachmentPositionSyncSystemTests.cs`
- `src/Tests/PresentationTests/MassNavigation/MassNavigationAttachedAuthorityTests.cs`

## 7. 证据

- `src/Core/Gameplay/Attachment/AttachmentOps.cs`
- `src/Core/Gameplay/Attachment/AttachmentPositionSyncSystem.cs`
- `src/Core/Components/AttachedLocalPose.cs`
- `src/Core/Components/MovementParticipation.cs`
- `src/Core/MassNavigation/Systems/MassNavigationPoseAuthorityBridge.cs`
- `src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs`
