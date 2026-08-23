# 实体系统

实体系统是 Ludots 中“实体如何组成、如何建立关系、如何交接位姿写权”的正式入口。本页只负责导航和边界；具体合同分别由关系、挂接、participant 和生命周期页面定义，避免出现第二份真相。

## 1. 概述

本入口覆盖四个相互关联但职责独立的能力：

- **实体关系**：关系类型、关系实体出生时的组件模板，以及 Association / Relationship 运行时。
- **实体挂接**：父子结构、局部位姿、挂接/解除挂接原子操作。
- **写权与成员身份**：`PoseAuthority` 的边界切换，以及挂接链对 MassNavigation 成员身份的挂起和恢复。
- **参与者与生命周期**：地图中的 team/player 代表实体和实体结构替换，分别复用既有正式合同。

本次实体关系与挂接合并的实现入口是 `RelationshipTypeTemplate`、`AttachmentOps`、`AttachmentPositionSyncSystem` 和 `MassNavigationPoseAuthorityBridge`。

## 2. 结构

```text
实体系统
├─ 实体关系与 Association
│  ├─ RelationshipCatalogConfig / RelationshipTypeTemplate
│  └─ 关系实体物化后的 GAS、指标、旗标和知识投影
├─ 实体挂接与 Pose Authority
│  ├─ ChildOf / ChildrenBuffer / AttachedLocalPose
│  ├─ AttachmentOps / AttachmentPositionSyncSystem
│  └─ PoseAuthority / MassNavigation 成员身份
├─ Map-Owned Participant Contract
└─ Entity Lifecycle 原子 Op
```

## 3. 详情

### 3.1 单一真相

| 问题 | 正式真相 | 不负责的层 |
| --- | --- | --- |
| 关系类型出生时带哪些组件 | `RelationshipCatalogConfig.Types[*].Template` 经 `ComponentRegistry` 编译的模板 | 不在运行时热路径解析 JSON |
| 谁挂在谁下面 | `ChildOf` 与父实体的 `ChildrenBuffer` | 不由 Presenter 树或 showcase 私有状态代替 |
| 子实体如何跟随父实体 | `AttachedLocalPose` + `AttachmentPositionSyncSystem` 写入 `WorldPositionCm` | 不把派生世界坐标重复存进局部组件 |
| 谁能写最终位姿 | `PoseAuthority`，经 `PoseAuthorityArbiter` 在固定步边界结算 | 不允许 Nav、Physics、GAS 位移和 Attachment 同时写 |
| 挂接链是否是导航成员 | `MassNavigationAgent` 等成员组件或 `SuspendedNavMembership` 快照 | 不复用旧 agent index，不在挂接系统私自重建求解器槽位 |
| team/player 是谁 | [Map-Owned Participant Contract](../architecture/map-owned-participant-contract.md) | 不新增全局 participant 容器 |
| 实体结构替换怎么组合 | [Entity Lifecycle 原子 Op](../architecture/entity-lifecycle-atomic-ops.md) | 不把挂接或关系模板扩展成生命周期 DSL |

### 3.2 运行时顺序

挂接链的固定步语义是：

1. `PoseAuthorityCommitSystem` 结算挂接/解除挂接写权。
2. `AttachmentPositionSyncSystem` 按父先子后的深度顺序，从父位姿和局部位姿派生子世界位姿。
3. MassNavigation 等后续系统只处理仍然拥有成员身份和相应写权的实体。
4. 网格、表现和其他派生链读取已经提交的 `WorldPositionCm`。

关系类型模板则在关系目录安装时编译一次，在关系实体物化时只应用已编译的组件值。

## 4. 场景

- 关系系统需要创建一条“敌对”或“盟友”关系时，类型模板为关系实体补上初始属性、标签或效果容器，身份组件仍由运行时拥有。
- 载具载着乘客移动时，乘客通过 `AttachmentOps.Attach` 成为父实体的子实体；乘客不再占用 MassNavigation 求解器成员槽位，而由挂接同步器从载具位姿派生。
- 乘客解除挂接时，系统恢复其导航成员身份，按当前已提交位姿重新播种；旧 agent index 不会被当作稳定身份继续使用。
- 任何预检、写权申请、成员身份变更或父子结构操作失败时，操作立即失败并恢复完整挂接状态，不留下半绑定实体。

## 5. 边界

- 关系模板不是关系规则 DSL；指标、旗标、回调、知识授权仍由关系目录和关系运行时各自负责。
- 挂接不是 Presenter 骨骼挂载；Presenter 层的 Attachment 只描述表现变换，Core 挂接合同见[实体挂接与 Pose Authority](attachment-and-pose-authority.md)。
- `AttachmentPositionSyncSystem` 不替代父实体生命周期系统；父实体死亡后的孤儿清理遵循 Core 既有生命周期标记和清理规则。
- 关系与挂接不新增 fallback。缺少父位姿、写权仲裁器、子关系或容量时，必须明确失败。

## 6. UAT

用户视角的可观察验收见[实体系统验收](uat.md)。实现证据：

- 关系模板：`src/Core/Gameplay/Relationships/RelationshipTypeTemplate.cs`、`src/Core/Gameplay/Relationships/Config/RelationshipCatalogConfig.cs`、`src/Tests/GasTests/Association/RelationshipTypeTemplateTests.cs`
- 挂接原子操作：`src/Core/Gameplay/Attachment/AttachmentOps.cs`、`src/Tests/GasTests/Effect/EntityAttachmentTests.cs`
- 位姿同步：`src/Core/Gameplay/Attachment/AttachmentPositionSyncSystem.cs`、`src/Tests/GasTests/Effect/AttachmentPositionSyncSystemTests.cs`
- MassNavigation authority：`src/Core/MassNavigation/Systems/MassNavigationPoseAuthorityBridge.cs`、`src/Tests/PresentationTests/MassNavigation/MassNavigationAttachedAuthorityTests.cs`
