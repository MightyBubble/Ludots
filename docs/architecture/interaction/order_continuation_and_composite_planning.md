# Order Continuation And Composite Planning

> SSOT scope: `CoreInputMod` raw order submit seam之后，通用的“复合命令规划 + 完成后续接”基建。  
> 本文只覆盖 `move-then-cast` 这类组合命令如何进入现有 GAS order 体系，不覆盖 MassNavigationFlow move runtime、路径可视化 performer、或 UI queue 面板。

## 1. 现状缺口

现有仓库已经具备这些可复用能力：

- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
  - 负责输入到原子 `Order` 的映射。
- `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs`
  - 负责把本地输入映射接到权威 `OrderQueue`。
- `src/Core/Gameplay/GAS/Orders/OrderQueue.cs`
  - 负责跨系统的入队。
- `src/Core/Gameplay/GAS/Components/OrderBuffer.cs`
  - 负责实体级 active / queued / pending order。
- `src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs`
  - 负责按照 order type config 把 order 写入 buffer / blackboard。
- `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`
  - 已经提供 `targeting.castRangeCm` 与 `targeting.allowMoveChase`，可用于通用施法几何规划。

但缺了一个通用层，把“玩家意图”翻译成“多个 order 的安全组合”：

- 超距技能需要先移动再施法。
- `Shift` 队列中的组合命令不能因为 order priority 反转而乱序。
- 输入系统本身不应该膨胀成 composite planner。

## 2. 复用清单

按 `docs/conventions/02_ai_assisted_development.md` §4，本切片显式复用：

- System: `InputOrderMappingSystem`
  - 继续只产出原子 order，不承接组合行为。
- Mod seam: `LocalOrderSourceHelper`
  - 作为 raw submit handler 的唯一接缝。
- Registry: `AbilityDefinitionRegistry`
  - 读取 `targeting.castRangeCm` 与 `targeting.allowMoveChase` 作为通用 cast 几何。
- Queue / Buffer: `OrderQueue`、`OrderBuffer`、`OrderSubmitter`
  - 不新增平行队列，不重写 order runtime。
- Phase: `SystemGroup.AbilityActivation`
  - 续接处理仍留在现有 order / ability 激活阶段。

## 3. 设计原则

### 3.1 输入层只做原子命令

`InputOrderMappingSystem` 只负责：

- 输入触发
- 目标采样
- 构建单个原子 `Order`

它不负责：

- move-then-cast
- 队列重排
- 预测执行起点

这些逻辑统一放到 `CompositeOrderPlanner`。

### 3.2 组合命令不直接塞两个 queued order

现有 `OrderBuffer.Enqueue(...)` 采用 priority 排序。  
如果直接把 `moveTo(priority=60)` 与 `castAbility(priority=100)` 一起排进 queued buffer，则 `castAbility` 会先于 `moveTo` 激活，破坏 `Shift` 队列语义。

因此本切片采用：

1. 先提交前置 `moveTo`
2. 把 follow-up `castAbility` 存入 continuation buffer
3. 当前置 order 完成时，再把 follow-up cast 重新提交回现有 order runtime

这样不需要发明第二套 queue，也不会依赖临时 hack。

## 4. Runtime 结构

### 4.1 Composite planner

`src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs`

职责：

- 接收 raw `Order`
- 若不是可规划的 cast，直接透传到 `OrderQueue`
- 若是超距 cast：
  - 解析 actor 当前或 projected 起点
  - 读取 ability targeting cast range 与显式 move-chase 策略
  - 计算最近合法施法点
  - 生成前置 `moveTo`
  - 注册 follow-up `castAbility`

规划规则：

- `Immediate` cast：基于 actor 当前真实位置规划。
- `Queued` cast：优先基于 `OrderBuffer` 中最后一个 move order 的预测终点规划。
- follow-up cast 以 `Queued` 模式重新提交，避免激活时清空后续 queue。

### 4.2 Continuation buffer

`src/Core/Gameplay/GAS/Components/OrderContinuationBuffer.cs`

新增：

- `OrderContinuationBuffer`
  - 以 `triggerOrderId -> follow-up Order` 形式保存后继命令。
- `CompletedOrderSignal`
  - 由完成 order 的 runtime 写入，通知后续系统消费 continuation。

### 4.3 Completion signal

`src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs`

`NotifyOrderComplete(...)` 在清理 active order 后：

- 若实体存在 continuation buffer 且仍有条目
- 写入 `CompletedOrderSignal`

它仍然复用原有 deactivate / promote 流程，不改现有 order 完成主语义。

### 4.4 Continuation submit system

`src/Core/Gameplay/GAS/Systems/OrderContinuationSystem.cs`

职责：

- 监听 `CompletedOrderSignal`
- 从 `OrderContinuationBuffer` 取出匹配 follow-up
- 通过现有 `OrderSubmitter.Submit(...)` 重新提交

这样 follow-up order 仍然经过：

- order rule 检查
- queued / pending 语义
- blackboard 写入

## 5. 当前覆盖的行为

本切片明确覆盖：

- `castAbility` 超距时自动拆成 move-then-cast
- `Shift` queued cast 依据 projected move endpoint 规划
- 前置 move 完成后，follow-up cast 通过现有 order runtime 重新提交

本切片不覆盖：

- MassNavigationFlow 驱动的真正移动执行
- queued path / hover path 的 performer 可视化
- 多段 continuation 链的专用 UI 展示
- “目标移动后重新规划 cast 点”的 runtime replanning

## 6. 代码锚点

- `src/Core/Gameplay/GAS/Orders/CompositeOrderPlanner.cs`
- `src/Core/Gameplay/GAS/Components/OrderContinuationBuffer.cs`
- `src/Core/Gameplay/GAS/Systems/OrderContinuationSystem.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs`
- `src/Core/Gameplay/GAS/Orders/OrderQueue.cs`
- `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs`
- `src/Core/Engine/GameEngine.cs`
- `src/Tests/GasTests/OrderCompositePlannerTests.cs`

## 7. 后续切片衔接

下一切片直接在这条基建上推进：

- `move-runtime-navigation`
  - 让 `moveTo` 不再直接 step `WorldPositionCm`，而是改由 MassNavigationFlow 执行。
- `order-indicator-and-acceptance`
  - 把移动路径、队列路径、接受范围等统一并入 indicator performer。
