# MassNavigation 数值域与确定性边界

## 1. 概述

本页是 MassNavigation 数值职责的正式裁决，收口 GitHub issue #671。

MassNavigationFlow 的私有求解状态允许使用 `float`。这是一项只覆盖大规模移动求解、避障、群组目标和到达判定的性能豁免，不是对全仓 gameplay 状态规则的放宽。对 MassNavigation 之外的玩法系统，`Fix64` 规则保持不变。

运行中的求解器数组是当前 fixed tick 内的执行真相；对 ECS、存档、网络和表现层公开的位置真相仍是 `WorldPositionCm`。MassNavigation 同一执行域内的 Formation、Road 与 Route 适配器可以通过 `GetAgentWorldPositionCm` 读取尚未发布的求解位置来继续规划，但这个观测口不是世界位置权威，也不得被其他玩法系统当作 ECS 状态替代品。两者只能通过本文规定的边界转换，不得向执行域外直接暴露求解器数组。

## 2. 结构

```text
业务订单 / MovePlanning 目标
  -> 有限值与厘米单位校验
  -> MassNavigation 私有 float 执行域
       -> SoA 位置、速度、目标、避障 scratch
       -> 群组距离与 Arrived 判定
  -> 边界 A: MathF.Round 到整数厘米
  -> Fix64Vec2 / WorldPositionCm
  -> ECS、表现、空间查询及其他玩法消费者

MassNavigation 私有 float Arrived
  -> 边界 B: 仅输出“订单已完成”离散结果
  -> OrderBuffer 完成状态
```

权威关系如下：

| 数据 | 运行期 owner | 对外权威 |
|---|---|---|
| agent 连续位置、速度、避障中间量 | `MassNavigationFlowSolverState` 的私有 SoA | 数组不公开；只允许同一执行域通过受控 runtime API 观测 |
| 群组目标、成员目标、`Arrived` | `MassNavigationGroupRuntime` | 只允许输出订单完成这一离散结果 |
| 实体世界位置 | `WorldPositionCm` | ECS 与其他模块唯一位置真相 |
| 存档、网络、回放输入 | 各正式基础设施 | 不得序列化 solver float 数组作为权威状态 |

## 3. 详情

### 3.1 float 豁免范围

允许使用 `float` 的范围只有：

- MassNavigationFlow 私有 SoA 中的位置、速度、方向、距离和避障中间量；
- MassNavigationGroupRuntime 内部的群组中心、成员执行目标、距离阈值和 `Arrived`；
- 从正式订单或 MovePlanning 进入 MassNavigation 后的厘米执行目标；
- 诊断与只读证据中的求解器采样值。

这些值不得直接成为组件、关系、属性、存档字段或网络复制字段。`GetAgentWorldPositionCm` 是明确的例外：它给 Formation、Road、Route 与诊断返回当前 solver 世界厘米，用于同一 runtime 内的下一步规划与证据比对；调用者不得保存为平行世界状态，也不得据此绕过 entity sync。新增其他跨模块数值必须先选择正式的定点或整数厘米合同，不得借用本豁免扩散 `float`。

### 3.2 WorldPositionCm 与 solver truth

一个 active runtime 内，solver float 位置负责推进下一次求解，因此它是 fixed tick 之间的内部执行真相。`WorldPositionCm` 是每次 entity sync 后发布给世界的外部位置真相。

发布规则固定为：solver world cm 先经过 `MathF.Round` 转成整数厘米，再用 `Fix64Vec2.FromInt` 写入 `WorldPositionCm`。Presentation、空间查询、碰撞、GAS 和执行域外的其他玩法模块只能消费这个已发布位置，不能反向引用 solver 数组。Formation、Road 与 Route 适配器若需要当前未量化位置，只能通过 `MassNavigationSimulationRuntime.GetAgentWorldPositionCm` 读取，不得持有 solver 或建立第二份长期位置状态。

从 ECS 重建 MassNavigation runtime 时，入口从 `WorldPositionCm` 读取 `Fix64`，转换成局部 `float` 作为新的执行初值。runtime 卸载后，私有 float 状态没有独立存续权。

### 3.3 Arrived 与订单完成

`Arrived` 使用 float 距离和配置阈值判定，并允许驱动 `OrderBuffer` 的完成通知。这是本裁决明确允许的 gameplay 可见结果。

跨边界的只有“完成或未完成”这一离散结果，不包含原始距离、速度或目标 float。到达阈值必须来自已校验配置；不得在订单系统中再做第二套到达计算，也不得因浮点误差添加静默 fallback。

### 3.4 Formation 订单编码与提交边界

Optional Core Formation 在写入 `FormationCommandState` 前，必须对同次更新的全部语义订单完成最终编码预检。共享 `OrderId` 的相对布局偏移先加入目标中心，再校验最终 X/Y 整数厘米；rotate 先校验并归一化 facing，再编码为整数微弧度。prepare/preflight 只写预分配 SoA scratch，全部成功后才按稳定顺序 commit 并完成 `OrderBuffer`。

任一编码失败必须报告 `OrderId`、Formation entity、字段和值，且本次更新内所有 Formation command state 与 active order 保持原值。禁止边编码边写 ECS、静默钳制、fallback、异常后的全量 ECS 快照回滚，以及热路径 `ToArray`、`Array.Resize` 或集合增长。

### 3.5 确定性边界

MassNavigation 的确定性承诺是：相同可执行版本、相同配置、相同 fixed tick 输入和相同初始 `WorldPositionCm` 下，worker 分片数量不得改变结果。求解器采用只读快照、互不重叠的输出区和 worker-local scratch，专项测试锁定串行与并行结果一致。

当前不承诺不同 CPU、操作系统、.NET 运行时或编译版本之间逐位一致，也不承诺用 solver float 快照做跨平台权威回放。因此：

- 网络同步和存档不得把 solver float 数组当作权威快照；
- 需要恢复时，应从正式订单、配置和最后发布的 `WorldPositionCm` 重建 runtime；
- 若未来产品要求跨平台逐 tick 确定性，必须另立数值域迁移议题，不能在本边界内静默宣称已经满足。

## 4. 场景

### 场景一：玩家下达移动订单

玩家选择一组单位并点击目的地。正式订单进入 MassNavigation 后，solver 用 float 高效推进单位；其他玩法和表现只看到同步后的 `WorldPositionCm`。当群组进入配置的到达范围，订单完成一次，单位不会因为两套位置真相重复完成或永不完成。

### 场景二：同一场战斗切换 worker 数

开发者在相同版本和输入下用单 worker 与多 worker 运行同一组单位。worker 只决定任务分片，不改变单位轨迹和到达结果。

### 场景三：恢复地图 runtime

地图 runtime 卸载后重新创建时，系统从正式配置、订单状态和 `WorldPositionCm` 重建求解器，不读取已经销毁的 float 数组，也不维护一份平行的世界位置存档。

## 5. 边界

- 本裁决不要求在 #671 中重写求解器数值实现。
- 本裁决不允许 MassNavigation Core 接管 Input、Selection、Presentation 或 Formation 职责。
- 本裁决不把 `float` 豁免扩展到生命值、资源、伤害、关系、计时、存档或网络权威状态。
- 诊断可以同时展示 solver world cm 与 ECS world cm，但必须明确标签，不能把诊断采样变成运行时输入。
- 任何新的 solver 对外数值出口都必须先定义单位、舍入、权威和恢复规则。

## 6. UAT

```gherkin
功能: MassNavigation 对外位置只有一个权威

  场景: 玩家观察正在移动的单位
    假如单位正在 MassNavigationFlow 中持续移动
    当 fixed tick 将求解结果同步到实体世界
    那么表现、存档、网络与执行域外玩法只读取 WorldPositionCm
    并且 Formation、Road 与 Route 只通过 GetAgentWorldPositionCm 做同一执行域的即时规划
    并且求解器 float 数组不会作为第二份公开世界位置
```

```gherkin
功能: 到达结果跨越数值边界

  场景: 一组单位到达玩家指定的目的地
    假如群组目标和到达阈值已经通过配置校验
    当 MassNavigationGroupRuntime 将 Arrived 判定为真
    那么 OrderBuffer 收到一次订单完成结果
    并且不会向订单系统公开原始 float 距离作为另一套裁决
```

```gherkin
功能: worker 分片不改变玩家结果

  场景: 相同战斗分别使用单 worker 与多 worker
    假如两个运行使用相同版本、配置、fixed tick 输入和初始 WorldPositionCm
    当双方执行相同数量的 MassNavigation fixed tick
    那么单位位置与到达结果一致
    并且 avoidance scratch 只按 worker 数预分配
```
