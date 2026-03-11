# 运行时实体生成链路

本篇定义 Ludots 当前的运行时实体生成标准流程，并明确它与地图初始化实体创建的职责边界。

核心结论：

*   地图初始化实体创建走 `MapLoader.LoadEntities(mapConfig)`。
*   运行时实体创建统一走 `RuntimeEntitySpawnRequest -> RuntimeEntitySpawnQueue -> RuntimeEntitySpawnSystem`。
*   `CreateUnit`、相机验收点击生成、未来的交互/脚本生成，都只能复用这条运行时链路，不允许各自直接写一套临时创建逻辑。
*   缺队列、队列满、引用未知类型/模板时直接失败，不做 fallback。

## 1 为什么要分成两条链路

Ludots 里存在两类完全不同的实体创建时机：

*   **地图初始化**：随地图加载一次性实例化静态内容和初始单位。
*   **运行时生成**：由 Effect、Input、Trigger、脚本或 Mod 逻辑在游戏运行中按事件请求生成。

这两类行为不能混写在一条路径里。

地图初始化需要依赖地图配置、Board、空间服务和地图生命周期；运行时生成则需要接受动态请求、延迟到系统阶段统一落地，并与 GAS / 输入 / 订单等运行时基建协同。

当前实现里，两条链路分别是：

*   地图初始化：`src/Core/Engine/GameEngine.cs:761`、`src/Core/Engine/GameEngine.cs:780`
*   运行时生成入口：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs:14`
*   运行时生成落地：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:17`

## 2 地图初始化链路

地图加载时，引擎顺序是：

```text
GameEngine.LoadMap(mapId)
  -> CreateBoardsForSession(session, mapConfig)
  -> ApplyBoardSpatialConfig(primaryBoard)
  -> MapLoader.LoadEntities(mapConfig)
```

代码位置：

*   `src/Core/Engine/GameEngine.cs:761`
*   `src/Core/Engine/GameEngine.cs:774`
*   `src/Core/Engine/GameEngine.cs:780`

这条链路的职责是：

*   从 MapConfig 读取静态 `Entities`
*   在地图 session 和 board 已建立后实例化实体
*   把实体纳入地图生命周期管理

这条链路**不是**运行时生成接口，运行时逻辑不得调用它。

## 3 运行时生成标准链路

运行时实体生成统一使用 `RuntimeEntitySpawnRequest`：

*   请求结构：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs:14`
*   请求队列：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs:27`
*   系统落地：`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:17`

标准流程如下：

```text
Producer
  -> 构造 RuntimeEntitySpawnRequest
  -> RuntimeEntitySpawnQueue.TryEnqueue(request)
  -> RuntimeEntitySpawnSystem.Update()
  -> 按 Kind 物化实体
  -> 可选发布 OnSpawn Effect
```

当前支持两种请求：

*   `RuntimeEntitySpawnKind.UnitType`：按 `UnitTypeId` 生成运行时单位，见 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs:7`
*   `RuntimeEntitySpawnKind.Template`：按 `TemplateId` 生成模板实体，见 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnQueue.cs:11`

`RuntimeEntitySpawnSystem` 的职责只有三件事：

*   校验请求是否合法，不合法直接抛错，见 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:55`、`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:81`
*   物化实体并补齐必要运行时组件，见 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:60`、`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:111`
*   继承团队、Map 归属，并在需要时发布 `OnSpawn` 效果，见 `src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:145`、`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:168`、`src/Core/Gameplay/Spawning/RuntimeEntitySpawnSystem.cs:194`

## 4 `CreateUnit` 的正确职责

`CreateUnit` 是 GAS 内建 handler，但它**不直接创建实体**。

当前职责只有：

*   根据 `EffectContext` 解析生成原点，见 `src/Core/Gameplay/GAS/BuiltinHandlers.cs:214`
*   计算散布偏移，见 `src/Core/Gameplay/GAS/BuiltinHandlers.cs:223`
*   把请求写入 `RuntimeEntitySpawnQueue`，见 `src/Core/Gameplay/GAS/BuiltinHandlers.cs:218`

如果运行时上下文里没有 `RuntimeEntitySpawnQueue`，直接抛错：

*   `src/Core/Gameplay/GAS/BuiltinHandlers.cs:205`
*   `src/Core/Gameplay/GAS/BuiltinHandlers.cs:208`

GAS 系统把这条依赖显式注入到 builtin handler 运行时上下文中：

*   `src/Core/Gameplay/GAS/BuiltinHandlerExecutionContext.cs:14`
*   `src/Core/Gameplay/GAS/BuiltinHandlerExecutionContext.cs:20`
*   `src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs:58`
*   `src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs:67`
*   `src/Core/Engine/GameEngine.cs:446`
*   `src/Core/Engine/GameEngine.cs:579`
*   `src/Core/Engine/GameEngine.cs:677`

这意味着：

*   Effect 负责表达“我要生成什么”
*   Spawn System 负责真正“怎么生成”
*   不允许把实体创建细节重新塞回 Effect handler 或 Mod 逻辑里

## 5 Mod 侧如何正确请求运行时生成

Mod 如果需要在运行时生成实体，也必须复用同一条队列。

相机 projection 验收场景的实现就是标准示例：

*   Mod 从 `CoreServiceKeys.RuntimeEntitySpawnQueue` 取队列，见 `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:156`
*   构造 `RuntimeEntitySpawnRequest`，见 `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:161`
*   用 `Template` 方式请求生成 `ProjectionSpawnTemplateId`，见 `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:163`
*   队列满直接失败，见 `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:169`

同一个点击还会额外发一个 performer cue marker，但那只是表现验收信号，不参与实体生成链路：

*   `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:121`
*   `mods/fixtures/camera/CameraAcceptanceMod/Runtime/CameraAcceptanceRuntime.cs:136`

## 6 当前合同

当前运行时生成合同明确如下：

*   运行时 gameplay 实体创建，统一通过 `RuntimeEntitySpawnRequest`
*   `CreateUnit` 当前合同是 `UnitTypeId` 生成，不扩展成“直接按任意模板名生成”
*   需要按模板生成时，显式使用 `RuntimeEntitySpawnKind.Template`
*   不允许为了某个 Mod、某张地图或某个验收流程增加第二条生成管线
*   不允许因为缺少上下文就退回 `World.Create(...)` 或 acceptance-local fallback

当前仍保留的边界说明见：

*   `artifacts/techdebt/2026-03-11-runtime-spawn-contract.md`

这份 debt 说明记录了“地图初始化实体创建”和“运行时生成”仍是两条职责不同的实现链路。这是当前有意保留的分层，不是缺陷掩盖。

## 7 验证证据

本次标准流程已有自动化证据：

*   `CreateUnit` 会入队运行时生成请求：`src/Tests/GasTests/TagEffectArchitectureTests.cs:376`
*   `RuntimeEntitySpawnSystem` 会生成实体并发布 `OnSpawn` 效果：`src/Tests/GasTests/TagEffectArchitectureTests.cs:410`
*   Projection 地图点击会生成运行时实体且 cue marker 会按时消失：`src/Tests/GasTests/Production/CameraAcceptanceModTests.cs:72`

如果后续有新的运行时生成来源，优先判断它只是新的 producer，还是需要扩展 `RuntimeEntitySpawnRequest` 合同。不要先写新系统，再事后合并。
