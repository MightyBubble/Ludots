# Trigger 开发指南

Trigger 体系用于把"事件"与"脚本化动作序列"连接起来。引擎支持 **Map 事件隔离**、**EventHandler**、**SystemFactoryRegistry** 和 **TriggerDecoratorRegistry**，定义了 Mod 与 Map 之间清晰的事件关系。

## 1 核心概念

*   **EventKey**：强类型事件键，忽略大小写比较。所有事件都以 EventKey 作为统一入口。
*   **GameEvents**：引擎内置事件集合（例如 GameStart、MapLoaded、MapUnloaded、MapResumed）。
*   **ScriptContext**：事件执行上下文，本质是 string 到 object 的轻量 KV 容器。
*   **ContextKeys**：上下文 key 的集中定义，用于避免业务散落 magic string。
*   **Trigger**：事件处理单元，包含条件、优先级与动作序列。**Map 是 Trigger 的唯一真相**——Trigger 声明在 MapDefinition/MapConfig 中，由引擎在 LoadMap 时实例化。
*   **EventHandler**：Mod 通过 `context.OnEvent()` 注册的简单回调，无条件/优先级/生命周期钩子。
*   **TriggerManager**：触发器注册中心与事件分发器，支持全局事件和 Map-scoped 事件。

关键代码位置：

*   TriggerManager：`src/Core/Scripting/TriggerManager.cs`
*   Trigger 与 TriggerBuilder：`src/Core/Scripting/Trigger.cs`、`src/Core/Scripting/TriggerBuilder.cs`
*   EventKey 与 GameEvents：`src/Core/Scripting/EventKey.cs`、`src/Core/Scripting/GameEvents.cs`
*   ScriptContext 与 ContextKeys：`src/Core/Scripting/ScriptContext.cs`、`src/Core/Scripting/ContextKeys.cs`
*   SystemFactoryRegistry：`src/Core/Engine/SystemFactoryRegistry.cs`
*   TriggerDecoratorRegistry：`src/Core/Scripting/TriggerDecoratorRegistry.cs`

## 2 Mod 注册方式

Mod 在 `OnLoad(IModContext context)` 中通过以下 API 注册事件处理：

### 2.1 OnEvent — 简单事件回调

```csharp
// 响应全局事件（GameStart、自定义事件等）
context.OnEvent(GameEvents.GameStart, async ctx =>
{
    var registry = ctx.Get<AbilityDefinitionRegistry>(ContextKeys.AbilityDefinitionRegistry);
    registry.Register(/* ... */);
});

// 响应 Map 事件（MapLoaded 会在每次 LoadMap 时由 FireMapEvent 触发）
context.OnEvent(GameEvents.MapLoaded, async ctx =>
{
    var mapTags = ctx.Get<List<string>>(ContextKeys.MapTags);
    if (mapTags?.Contains("moba") != true) return;
    // 只在 moba 地图执行的逻辑
});
```

EventHandler 的特点：
*   **无条件过滤**——逻辑自行在 handler 内部判断
*   **无优先级**——执行顺序由注册顺序决定
*   **同时响应全局和 Map-scoped 事件**——`FireMapEvent` 也会触发 EventHandler

### 2.2 SystemFactoryRegistry — 两级 System 注册

Mod 注册 System 工厂，Map Trigger 按需激活：

```csharp
// 在 OnLoad 中注册工厂（不立即创建 System）
context.SystemFactoryRegistry.Register("MobaOrderSource", SystemGroup.AbilityActivation,
    ctx => new MobaLocalOrderSourceSystem(ctx.GetWorld()));

context.SystemFactoryRegistry.RegisterPresentation("EntityClickSelect",
    ctx => new EntityClickSelectSystem(ctx.GetWorld()));
```

激活由 Map Trigger 或 EventHandler 完成：
```csharp
context.OnEvent(GameEvents.MapLoaded, ctx =>
{
    var sfr = ctx.Get<SystemFactoryRegistry>(ContextKeys.SystemFactoryRegistry);
    var engine = ctx.GetEngine();
    sfr.TryActivate("MobaOrderSource", ctx, engine);  // 幂等，重复激活返回 false
    return Task.CompletedTask;
});
```

### 2.3 TriggerDecoratorRegistry — Mod 修饰 Map Trigger

Mod 不直接创建 Trigger，而是"修饰"Map 声明的 Trigger：

```csharp
// 按类型匹配
context.TriggerDecorators.Register<BattleSetupTrigger>(t => {
    t.Priority = -20;  // 调整优先级
});

// 按类型名匹配（适用于 JSON 定义的 Trigger）
context.TriggerDecorators.Register("BattleSetupTrigger", t => {
    t.AddAction(new MyExtraCommand());
});

// 锚点注入（在 Trigger 的 AnchorCommand 后面插入命令）
context.TriggerDecorators.RegisterAnchor("map_ready",
    new Setup3CCameraCommand());
```

## 3 事件触发点

### 3.1 全局事件（FireEvent）

引擎在关键生命周期点触发全局事件：

*   `GameEngine.Start()` → `FireEvent(GameEvents.GameStart)`
*   预算熔断等异常路径 → 特定事件

全局事件会触发：
1. 所有匹配 EventKey 的 EventHandler
2. 所有匹配 EventKey 的全局 Trigger（按 Priority 升序）

### 3.2 Map-scoped 事件（FireMapEvent）

Map 生命周期事件使用 `FireMapEvent`，**只触发指定 Map 的 Trigger + 所有 EventHandler**：

*   `GameEngine.LoadMap(mapId)` → `FireMapEvent(mapId, GameEvents.MapLoaded)`
*   `GameEngine.UnloadMap(mapId)` → `FireMapEvent(mapId, GameEvents.MapUnloaded)`
*   Map 恢复焦点 → `FireMapEvent(restoredMapId, GameEvents.MapResumed)`

```
FireMapEvent(mapId, MapLoaded, ctx)
  ├── 1. 触发所有匹配 MapLoaded 的 EventHandler（Mod 回调）
  └── 2. 触发 mapId 名下注册的 MapLoaded Trigger（按 Priority 升序）
```

**关键隔离**：`FireMapEvent` 不会触发其他 Map 的 Trigger，也不会触发全局注册的 Trigger。只有 EventHandler 是跨 Map 的。

## 4 优先级排序

Trigger 的 `Priority` 属性控制执行顺序：**值越小越先执行**。

```
Priority 0~50:   基础设置（激活 System、spawn 实体）
Priority 51~99:  玩法初始化
Priority 100+:   后处理（相机、UI、HUD）
```

FireEvent 和 FireMapEvent 都会按 Priority 升序排序后执行。

## 5 条件与动作

Trigger 的典型结构：

*   Conditions：`Func<ScriptContext, bool>` 列表，决定是否执行
*   Actions：GameCommand 列表或委托序列，按顺序执行
*   Priority：int，控制执行顺序

建议把"是否执行"的判断放在条件中，把"真正的开销逻辑"放在动作中。

## 6 扩展既有流程

当你需要在一个既定 Trigger 的动作序列中插入新动作时，优先使用 **TriggerDecoratorRegistry**：

*   `TriggerDecorators.Register<T>(decorator)` 按类型匹配并修改
*   `TriggerDecorators.Register(typeName, decorator)` 按名称匹配
*   `TriggerDecorators.RegisterAnchor(key, command)` 在锚点处注入

这使得多个 Mod 可以在不互相覆盖的情况下协作扩展同一条流程。

## 7 FireEvent 与 FireEventAsync

TriggerManager 提供两种触发方式：

*   `FireEvent(eventKey, ctx)` / `FireMapEvent(mapId, eventKey, ctx)`：异步触发但不等待完成；异常会被收集到 `TriggerManager.Errors`，不向上抛出。
*   `FireEventAsync(eventKey, ctx)` / `FireMapEventAsync(mapId, eventKey, ctx)`：等待所有触发器完成；异常会向上传播，同时也会记录到 `Errors`。

建议：

*   想要"不阻塞主循环"的场景用 `FireEvent`/`FireMapEvent`，并用 `Errors` 做可观测性。
*   需要"失败可见且能中止流程"的场景用 Async 版本。

## 8 Map 并存模型

引擎支持多 Map 同时存在（焦点栈模型）：

```
MapSessionManager
  ├── "strategic" (Active)     ← 战略图持续运行
  ├── "battle_42" (Active)     ← 战斗副本同时存在
  └── _focusStack: [strategic, battle_42]  ← battle_42 在栈顶有焦点

API:
  LoadMap("strategic")    → 创建 session, 入栈, FireMapEvent(strategic, MapLoaded)
  LoadMap("battle_42")    → 创建 session, 入栈, strategic 被 Suspend
                            FireMapEvent(battle_42, MapLoaded)
  UnloadMap("battle_42")  → FireMapEvent(battle_42, MapUnloaded), 清理, 弹栈
                            strategic 恢复 Active
```

*   **LoadMap 是添加式的**——不会卸载旧 Map
*   **UnloadMap 是显式的**——需要明确调用
*   **SuspendedTag**——暂停的 Map 的实体会被加上 SuspendedTag，恢复时移除
*   **实体清理按 MapId 过滤**——`MapSession.Cleanup` 只销毁 `MapEntity.MapId` 匹配的实体

## 9 开发规范

*   事件键与上下文 key 一律使用 `EventKey`、`GameEvents`、`ContextKeys`，不要在业务代码里散落字符串。
*   Trigger 按 Priority 升序执行，优先级设计要避免隐式依赖。
*   谨慎使用 `GameEvents.Tick`：它是高频事件，容易导致性能问题，优先用系统或时钟域解决。
*   Mod 必须使用 `OnEvent()`、`SystemFactoryRegistry` 或 `TriggerDecorators` 注册扩展——不要直接调用 `TriggerManager.RegisterTrigger()`。
