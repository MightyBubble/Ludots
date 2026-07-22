# MassNavigation 数值域与确定性边界

本页定义 MassNavigationFlow 的私有 `float` 豁免，以及 GAS/MovePlanning/MassNavigation 的 typed 边界。

## 数值 owner

| 数据 | Owner | 对外合同 |
| --- | --- | --- |
| solver 位置、速度、避障 scratch | `MassNavigationFlowSolverState` 私有 SoA | 不公开、不存档、不网络复制 |
| command group 目标与 arrival | `MassNavigationGroupRuntime` | `MovePlanExecutionResult` |
| Order lifecycle | GAS | `OrderBuffer`、完成/取消 |
| 实体世界位置 | `WorldPositionCm` | ECS 唯一公开位置真相 |

## 正式边界

```text
GAS Order spatial WorldCm
  -> finite centimeter validation
  -> MovePlanExecutionIntent
  -> MassNavigation private float execution
  -> MovePlanExecutionResult(Arrived/Failed)
  -> GAS completes or cancels Order

solver world cm
  -> MathF.Round
  -> Fix64Vec2 / WorldPositionCm
  -> gameplay and presentation consumers
```

MassNavigation 不接收 `OrderId` 或 `OrderTypeId`。`CommandGroupToken` 只是 opaque correlation token；GAS 负责 token 与 active order 的映射和匹配。

## MovePlan execution mode

- `None = 0`：未声明，任何 consumer 都不得接受；
- `Individual`：Road 等逐实体规划；
- `CommandGroup`：GAS cluster order projection。

Producer 必须显式写 mode，不能依赖 struct 默认值。

## Prepare/commit

Command-group execution 在任何 group/solver 写入前完成：

1. token、team、成员唯一性和 binding 校验；
2. 每个成员最终槽位目标计算；
3. group/member/focus/route capacity 校验；
4. route agent type 与最终目标校验；
5. 只对全部通过的 command group 提交。

Route 拒绝写 `MovePlanExecutionResultKind.Failed`，不先修改 group、focus 或 solver。容量不足明确失败；热路径不扩容、不丢弃、不使用 ECS 全量快照 fallback。

## Arrived 与 Failed

- `Arrived` 由 MassNavigation 的 float 距离与配置阈值判定，只输出离散 typed result；
- `Failed` 携带 `MovePlanFailureReason`；
- GAS lifecycle 只处理与 active order token 匹配的 result；
- 完成会发出 completed signal；取消不会伪装成完成，并删除该 order 的 continuation。

## WorldPositionCm

Solver float 是 active runtime 内部执行真相；`WorldPositionCm` 是执行域外唯一位置真相。

- Presentation、空间查询、存档、网络和其他 gameplay 只读 `WorldPositionCm`；
- Road 等同一执行域 adapter 可通过受控 runtime API 读取即时 solver world cm，但不得保存为第二份长期状态；
- runtime 恢复从配置、正式 order 状态和最后发布的 `WorldPositionCm` 重建，不恢复 solver float 数组。

## 确定性

相同版本、配置、fixed tick 输入和初始 `WorldPositionCm` 下，worker 分片数量不得改变结果。当前不承诺跨 CPU、OS、.NET 或编译版本逐位一致。

## UAT

```gherkin
Feature: typed result 跨越数值边界

  Scenario: command group 到达
    Given GAS 已投影有限厘米目标和显式 CommandGroup mode
    When MassNavigation 判定 command group arrived
    Then MassNavigation 只写 Arrived typed result
    And GAS 完成 token 匹配的 active order
    And Order 系统不重新计算 float 到达距离

  Scenario: route 预检失败
    Given 某成员没有可用 route agent type
    When command group 执行 prepare
    Then group 和 solver 不发生部分写入
    And MassNavigation 写 Failed typed result
    And GAS 取消 order 且不触发 continuation
```
