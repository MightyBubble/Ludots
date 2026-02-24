# Trigger 开发指南

Trigger 体系用于把“事件”与“脚本化动作序列”连接起来，支持 Mod 在加载期注册事件处理逻辑，并通过 ScriptContext 获取运行时依赖与状态。

## 1 核心概念

*   **EventKey**：强类型事件键，忽略大小写比较。所有事件都以 EventKey 作为统一入口。
*   **GameEvents**：引擎内置事件集合（例如 GameStart、MapLoaded）。
*   **ScriptContext**：事件执行上下文，本质是 string 到 object 的轻量 KV 容器。
*   **ContextKeys**：上下文 key 的集中定义，用于避免业务散落 magic string。
*   **Trigger**：事件处理单元，包含条件与动作序列。
*   **TriggerManager**：触发器注册中心与事件分发器。

关键代码位置：

*   TriggerManager：`src/Core/Scripting/TriggerManager.cs`
*   Trigger 与 TriggerBuilder：`src/Core/Scripting/Trigger.cs`、`src/Core/Scripting/TriggerBuilder.cs`
*   EventKey 与 GameEvents：`src/Core/Scripting/EventKey.cs`、`src/Core/Scripting/GameEvents.cs`
*   ScriptContext 与 ContextKeys：`src/Core/Scripting/ScriptContext.cs`、`src/Core/Scripting/ContextKeys.cs`

## 2 注册方式

Trigger 通常由 Mod 在加载期注册：

*   ModLoader 加载 Mod 程序集并调用入口的 `OnLoad(...)`
*   在 `OnLoad` 里通过 ModContext 获取 TriggerManager，注册 Trigger

示例 Mod：

*   `src/Mods/ExampleMod/ExampleModEntry.cs`
*   `src/Mods/ExampleMod/Triggers/ExampleTrigger.cs`

## 3 事件触发点

引擎在关键生命周期点触发事件：

*   `GameEngine.Start()` 会触发 `GameEvents.GameStart`
*   `GameEngine.LoadMap()` 成功后触发 `GameEvents.MapLoaded`
*   预算熔断等异常路径会触发特定事件（例如 SimulationBudgetFused）

## 4 条件与动作

Trigger 的典型结构：

*   Conditions：`Func<ScriptContext, bool>` 列表，决定是否执行
*   Actions：GameCommand 列表或委托序列，按顺序执行

建议把“是否执行”的判断放在条件中，把“真正的开销逻辑”放在动作中。

## 5 扩展既有流程

当你需要在一个既定 Trigger 的动作序列中插入新动作时，优先使用锚点与插入 API，而不是复制整段逻辑：

*   `TriggerBuilder.Anchor(key)` 添加锚点
*   `Trigger.OnAnchor(key, command)` 在锚点处插入命令
*   `Trigger.InsertBefore<TCommand>(...)`、`Trigger.InsertAfter<TCommand>(...)` 在某类命令前后插入

这使得多个 Mod 可以在不互相覆盖的情况下协作扩展同一条流程。

## 6 FireEvent 与 FireEventAsync

TriggerManager 提供两种触发方式：

*   `FireEvent(eventKey, ctx)`：异步触发但不等待完成；异常会被收集到 `TriggerManager.Errors`，不向上抛出。
*   `FireEventAsync(eventKey, ctx)`：等待所有触发器完成；异常会向上传播，同时也会记录到 `Errors`。

建议：

*   想要“不阻塞主循环”的场景用 `FireEvent`，并用 `Errors` 做可观测性。
*   需要“失败可见且能中止流程”的场景用 `FireEventAsync`。

## 7 开发规范

*   事件键与上下文 key 一律使用 `EventKey`、`GameEvents`、`ContextKeys`，不要在业务代码里散落字符串。
*   不要依赖 Trigger 的执行顺序：当前实现按“注册顺序快照遍历”触发，Trigger.Priority 未作为排序依据。
*   谨慎使用 `GameEvents.Tick`：它是高频事件，容易导致性能问题，优先用系统或时钟域解决。

