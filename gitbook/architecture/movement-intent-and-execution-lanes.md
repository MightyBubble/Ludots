# 移动意图与 per-entity 执行车道

Epic：[#769](https://github.com/MightyBubble/Ludots/issues/769)

本文是移动**意图层**、**客观身体层**与 **per-entity 执行车道**的正式合同。不含玩法角色语义；模板只选择执行档。

相关：

- 参与与写权基线：[`entity-simulation-layering.md`](entity-simulation-layering.md)
- MassNavigation↔Physics2D 热区/障碍交接：[#643](https://github.com/MightyBubble/Ludots/issues/643)

## 1 概述

- 意图回答「想怎么动 / 想面朝哪」。
- 客观身体回答「现在在哪 / 现在面朝哪」。
- 执行车道回答「谁有权把意图变成客观身体」。
- 同一固定步同一实体只有一个 `PoseAuthority` 写 `WorldPositionCm`。

## 2 结构

```text
控制 / 订单 / AI
  → MoveIntent
  → FacingIntent
  → LookIntent（二期）
        ↓ 只读
执行车道（per-entity）
  Nav | Motor | Physics | Displacement
        ↓
客观身体
  WorldPositionCm
  FacingDirection
  （热路径缓存）MassNav SoA / Position2D / Rotation2D / Velocity2D
        ↓ 可选
Kinematic 跟拍（physicsPresence=Kinematic）
```

## 3 详情

### 3.1 意图组件

| 组件 | 含义 | 谁写 |
|---|---|---|
| `MoveIntent` | `None` / `Direction` / `TargetPoint` + 期望速率 | 控制、订单适配、AI |
| `FacingIntent` | `None` / `ExplicitYaw` / `FollowMoveDirection` | 同上 |
| `LookIntent` | 视线偏航 | 二期 |

规则：

1. 意图不是位置真相。
2. 执行器只读意图，不写意图。
3. MassNav 步进内 `desiredVelocity` 是求解器私有量，不是意图 SSOT。
4. MassNav per-agent 目标点与 `MoveIntent.TargetPoint` 的收敛见 Epic 切片 4；在收敛完成前，Nav 档仍可按既有目标点 API 执行，但不得再发明第三套「意图通道」。

### 3.2 客观身体

| 组件 | 含义 | 谁写 |
|---|---|---|
| `WorldPositionCm` | 在哪 | 当前 `PoseAuthority` |
| `FacingDirection` | 身体现在朝哪 | Facing 求解按写权规则提交 |
| `Velocity2D` / Nav 速度 | 现在怎么动 | 当前执行器热路径 |

### 3.3 执行档（authoring）

`MovementParticipation` 增加中性字段 `execution`：

| `execution` | 允许的 `physicsPresence` | 初始 `PoseAuthority` |
|---|---|---|
| `nav` | `none` / `kinematic` | `Nav` |
| `motor` | `none` / `kinematic` | `Motor` |
| `physics` | `dynamic` | `Physics` |

非法配对启动失败（fail-fast）。

`PoseAuthorityKind` 增加 `Motor`。位移窗口可从 `Nav` 或 `Motor` 开启，结束后交还**开启前的执行写权**（`ResumeAuthority`），不再写死交还 Nav。

### 3.4 现网路径澄清

`execution=nav` + `physicsPresence=kinematic`（现网 Crowd Arena）语义是：

1. MassNavigation 积分并写 `WorldPositionCm`
2. Kinematic 桥把已提交位姿喂给物理
3. 物理从 Δpose 派生线速度用于推挤；**不是**「Nav 出期望速度、物理积分位置」

若实体需要「意图→物理积分写位置」，必须显式 `execution=physics`（并满足 #643 合同），不得静默改写 nav+kinematic 语义。

### 3.5 MassNavigation 绑定

仅 `execution=nav` 的实体可绑定为 nav agent。`motor` / `physics` 绑定失败。

## 4 场景

见 Epic #769 场景 1–5。

## 5 边界

- 禁止业务角色名进入 Core 执行枚举。
- 禁止 Nav 与 Motor 同时作为同一实体位置作者。
- 禁止意图与 `WorldPositionCm` 双真相。
- 热路径：SoA / Chunk / 零分配；写权切换只在固定步边界经 `PoseAuthorityArbiter` 结算。

## 6 UAT

Epic #769 第 6 节 Cucumber 场景为本合同验收语言；实现切片须把对应场景落成生产路径测试或合同测试。
