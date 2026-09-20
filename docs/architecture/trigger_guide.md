# Trigger 开发指南

Trigger 体系用于把"事件"与"脚本化动作序列"连接起来。引擎支持 **Map 事件隔离**、**EventHandler**、**SystemFactoryRegistry** 和 **TriggerDecoratorRegistry**，定义了 Mod 与 Map 之间清晰的事件关系。

## 1 核心概念

*   **EventKey**：强类型事件键，忽略大小写比较。所有事件都以 EventKey 作为统一入口。
*   **GameEvents**：引擎内置事件集合（例如 GameStart、MapLoaded、MapUnloaded、MapResumed）。
*   **ScriptContext**：事件执行上下文，本质是 string 到 object 的轻量 KV 容器。
*   **ContextKeys**：上下文 key 的集中定义，用于避免业务散落 magic string。
*   **Trigger**：事件处理单元，包含条件、优先级与动作序列。TriggerGraph 是统一作者面；挂载域由声明位置决定：地图/实体随 MapSession，技能由 `abilities.json` 定义并按 AbilityId 过滤，Mod 由 `mod.json` 定义并按 ModId 隔离。
*   **EventHandler**：Mod 通过 `context.OnEvent()` 注册的简单回调，无条件/优先级/生命周期钩子。
*   **TriggerManager**：触发器注册中心与事件分发器，支持全局事件和 Map-scoped 事件。

关键代码位置：

*   TriggerManager：`src/Core/Scripting/TriggerManager.cs`
*   Trigger 与 TriggerBuilder：`src/Core/Scripting/Trigger.cs`、`src/Core/Scripting/TriggerBuilder.cs`
*   EventKey 与 GameEvents：`src/Core/Scripting/EventKey.cs`、`src/Core/Scripting/GameEvents.cs`
*   ScriptContext 与 ContextKeys：`src/Core/Scripting/ScriptContext.cs`、`src/Core/Scripting/ContextKeys.cs`
*   SystemFactoryRegistry：`src/Core/Engine/SystemFactoryRegistry.cs`
*   TriggerDecoratorRegistry：`src/Core/Scripting/TriggerDecoratorRegistry.cs`
*   TriggerGraph mount contract：`src/Core/Gameplay/MapTriggers/TriggerGraphMount.cs`、`src/Core/Gameplay/MapTriggers/TriggerGraphMounting.cs`、`src/Core/Gameplay/MapTriggers/TriggerGraphMountTrigger.cs`

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
context.SystemFactoryRegistry.Register("TabTargetCycle", SystemGroup.InputCollection, ctx =>
{
    var engine = ctx.GetEngine() ?? throw new InvalidOperationException("GameEngine is required.");
    return new TabTargetCycleSystem(engine.World, engine.GlobalContext, engine.SpatialQueries);
});

context.SystemFactoryRegistry.RegisterPresentation("SkillBarOverlay", ctx =>
{
    var engine = ctx.GetEngine() ?? throw new InvalidOperationException("GameEngine is required.");
    return new SkillBarOverlaySystem(engine.World, engine.GlobalContext);
});
```

激活由 Map Trigger 或 EventHandler 完成：
```csharp
context.OnEvent(GameEvents.MapLoaded, ctx =>
{
    var sfr = ctx.Get<SystemFactoryRegistry>(ContextKeys.SystemFactoryRegistry);
    var engine = ctx.GetEngine();
    sfr.TryActivate("TabTargetCycle", ctx, engine);  // 幂等，重复激活返回 false
    return Task.CompletedTask;
});
```

约束：

- 消费玩家**逻辑输入**的 system 必须注册到 `SystemGroup.InputCollection`，并读取 `CoreServiceKeys.AuthoritativeInput`。
- `CommandSourceAcquisitionSystem`、`GasInputResponseSystem` 位于 `src/Core/Input/...`，属于 fixed-step 输入系统，不应作为 presentation system 注册。
- presentation system 只承载 HUD / overlay / presenter 等渲染侧逻辑，不直接消费 live `PlayerInputHandler`。

### 2.3 TriggerDecoratorRegistry — Mod 修饰既有 Trigger

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

默认 `route: "local"`（可省略）只挂在来源地图的 map/entity/ability 表。跨地图不要写挂载 `route: "global"`——该字段已退役，加载失败关闭；正式路径是事件 schema 的 `Scope: Global` + `FireGlobalEvent`，点对点用 `FireCrossMapEvent`。Mod 域图走 `FireEvent` 的全局索引，并用 `ModId` 做生命周期隔离。

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
*   global 地图图在注册地图卸载时先移除；Mod 图在 Mod 卸载前先移除。能力图随每张地图的 TriggerManager 注册和回收，不在施法热路径动态改结构。

## 9 开发规范

*   事件键与上下文 key 一律使用 `EventKey`、`GameEvents`、`ContextKeys`，不要在业务代码里散落字符串。
*   Trigger 按 Priority 升序执行，优先级设计要避免隐式依赖。
*   高频节奏需求不要造逐帧事件；地图节奏走 ThinkWave 时钟域（见第 10 节），`GameEvents.Tick` 已删除。
*   Mod 必须使用 `OnEvent()`、`SystemFactoryRegistry` 或 `TriggerDecorators` 注册扩展——不要直接调用 `TriggerManager.RegisterTrigger()`。

## 10 TriggerGraph 事件词汇表（ThinkWave 时钟域）

`MapHeartbeatClockSystem`（注册于 `SystemGroup.DeferredTriggerCollection`）为每张 Active 的地图按固定步长累计 tick；每张地图可在 MapConfig 里声明 `"HeartbeatIntervalTicks"`（整数 ≥1，缺省 30）。Suspended 的地图不推进、不触发。每个 wave 依次触发以下 Map-scoped 事件（payload key 见 `MapTriggerEventPayloadKeys`）：

| 事件 | 触发时机 | Payload |
|------|----------|---------|
| `EntitySpawned` | wave 内加入地图的实体 | `SourceEntity`、`SourceTeamId` |
| `EntityDied` | wave 内被销毁的实体（flush 时实体可能已回收，Team 在销毁时读取） | `SourceEntity`、`SourceTeamId` |
| `EntityAliveCountChanged` | 某队伍存活数（带 `AttributeBuffer`）与上一 wave 不同 | `SourceTeamId`、`Count`、`Delta` |
| `MapHeartbeat` | wave 收尾（供消费方读取本 wave 汇总） | `HeartbeatIndex` |
| `RegionEntered` / `RegionExited` | 区域系统（进入/离开 region） | `SourceEntity`、`RegionId` |

Spawn/death 观察来自 World 结构事件（`ComponentAdded<MapEntity>` / `EntityDestroyed`），覆盖全部正式 spawn 路径；每地图队列上限 1024，溢出计入可读的丢弃计数（`MapHeartbeatClockSystem.GetDroppedLifecycleEvents` / `TotalDroppedLifecycleEvents`）。

### 10.1 入口过滤（entries[].filters）

TriggerGraph 图的 entry 可声明可选 `filters` 块（`region`/`tag`/`team`/`threshold`/`direction` 全部可选，未知字段在作者面拒绝；`threshold` 与 `direction`（`cross_above`|`cross_below`）必须成对声明，`direction` 拼写由编译器与 GraphProgramRegistry 双重校验）。分发时由 `TriggerGraphEntryFiltersEvaluator.Matches` 评估：任一声明的过滤项在 payload 缺失或不相等时不匹配（fail closed，不抛错）。`tag` 目前没有任何事件携带 tag payload，声明即永不匹配——待 tag 型事件落地后放开。

## 11 TriggerGraph 域与时序合同

TriggerGraph 只有一套 VM 和一条 `TriggerManager` 分发路径。挂载位置决定作用域和生命周期，不改变图的作者面：

| 域 | 作者入口 | 作用域 | 事件入口 | 回收时机 |
|---|---|---|---|---|
| map | 地图 `TriggerGraphs[]` | 当前 `MapSession` | 地图生命周期、ThinkWave、GAS 桥 | 地图卸载 |
| entity | 实体模板或地图 entity mount | 实体自身及其 `ChildOf` 子树 | 出生/死亡当拍、ThinkWave、GAS 桥 | 实体死亡或地图卸载 |
| ability | `GAS/abilities.json.triggerGraphs` | 施法者 | `Ability.*`、`Effect.*`、GAS 事件 | 地图卸载；不随一次施法动态改结构 |
| mod | `mod.json.triggerGraphs` | Mod 全局索引，事件用 `ModId` 隔离 | `ModLoaded` 及全局事件 | Mod 卸载前 |

同一实体可以声明多张图。列表顺序就是挂载顺序；任何重复、空白或未登记图名都在加载时失败关闭。实体 attachment 只定义成员身份、父子关系和位姿，不能偷偷改变图的作用域。

## 12 固定 Tick 时序

模拟帧内顺序固定如下，图作者据此推理“本拍看到什么”：

```text
输入/命令写入
  -> GameplayEventDispatch 换页
  -> GAS 事件桥：把本拍 GameplayEvent 转成 Gas.Event.*
  -> Ability/Effect 时刻桥：把本拍生命周期镜像转成 Ability.* / Effect.*
  -> DeferredTriggerCollection：地图、实体、技能、Mod 图在模拟组内联执行
  -> 图内 SendEvent 写入下一拍缓冲
  -> ReactionSystem 读取本拍视图并提交 GAS 副作用
  -> Presenter 投影读取本拍全部副作用
  -> PresenterRuleSystem 在每个渲染帧只读并消费表现缓冲
```

桥接事件是换页后的本拍视图；图中再次发送的 GAS 事件严格下一拍可见。图只能在模拟组触发 `Fire*`，表现层、客户端和适配器不得反向开火。实体出生/死亡是生命周期点的同步分发，不等待 ThinkWave；实体图的持续执行仍共用所属地图的 ThinkWave，不创建实体级时钟。

## 13 GAS 与表现层边界

GAS 桥是事件的唯一转换点，不能由每个生产者重复调用 `TriggerManager`。技能图以 `AbilityId` 过滤并把施法者作为 caster；Mod 图以 `ModId` 过滤；global 地图图只扩大地图事件接收范围，不改写来源 `MapId` 或实体载荷。

图的写入在模拟组内提交，GAS 反应系统遵循既有一步滞后；表现投影可以看到本拍已提交的全部副作用。Presenter 只读 ECS/表现缓冲，不写地图变量、GAS 状态，也不触发 TriggerGraph。

## 14 时序 UAT

```gherkin
Feature: TriggerGraph 跨域时序可推理

  Scenario: GAS 桥看到本拍事件
    Given 第 N 拍 GameplayEventDispatch 已换页
    When 事件桥发布 Gas.Event.Combat.Hit
    Then 地图、实体和技能域图都能在第 N 拍读取该事件
    And 图内再次发送的事件只能在第 N+1 拍被读取

  Scenario: 技能图按施法者隔离
    Given 两个施法者都挂有同一 AbilityId 的 TriggerGraph
    When 其中一个施法者完成技能
    Then 只有该施法者的图执行
    And 另一个施法者的状态不变

  Scenario: 实体 attachment 子世界归属
    Given 一个聚落根实体带三张图并挂接居民子实体
    When 居民收到命中事件
    Then 根图可以看到该事件并更新聚落状态
    And 地图外实体的同名事件不会污染该状态

  Scenario: 表现层只读
    Given 本拍图已经写入状态并产生表现事件
    When PresenterRuleSystem 在渲染帧消费缓冲
    Then 画面显示本拍副作用
    And Presenter 不触发任何 TriggerGraph
```
