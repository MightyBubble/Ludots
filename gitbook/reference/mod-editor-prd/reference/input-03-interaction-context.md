# input-03 reference · 交互上下文档案

> 现状参考。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；配置说明见 [input-03 配置说明](../config/input-03-interaction-context.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `activeCollectionKey` / `activeEntityViewKey` / `filterProfileId`（可选，空=直通）/ `inputContextId`（可选）/ `commandIntentId`（可选）。
- 生命周期：声明了 `interactionContextProfile` 的能力在 exec 期间由 `AbilityExecInteractionContextSystem` 把档案挂载为实体状态——`InteractionContextInstance` 稀疏组件落在 exec 载体的控制域 representative 上（每域最新激活的 exec 胜出，LIFO）；exec 因完成、打断、换单或死亡结束后的下一次系统更新即回收。交互上下文栈已删除，帧机制全部实体化。
- 能力侧声明：abilities 的 exec 段写 `interactionContextProfile`（非空串校验在能力加载期）；档案名不存在在执行开始时抛错（非启动期）。档案的全部 id 字段（集合键 / 过滤档案 / 命令意图）在档案安装期解析，未知引用启动期失败。
- 命令意图联动：仲裁器读实体挂载交互状态（DEC-14）：挂载上下文的显式意图优先，其次玩家默认（InteractionPref），挂载且零意图不路由不冒泡；无挂载（steady state）用玩家默认。
- 输入上下文联动：`InputContextProjectionSystem` 每 tick 把 possessed representative 挂载上下文的 `inputContextId` 需求并入对应座位的 (seatId, contextId, op) diff 命令流；上下文回收后下一 tick 弹出。这是唯一的帧→IMC 翻译点，代码不得绕过。
- 集合路由：`ContextBoundCollectionWriter` 读挂载上下文的过滤档案与集合键；steady state 读数据声明的保留默认档案（`interaction.context.default`，引擎安装，永不挂载，缺席的挂载组件即 steady state）路由到 `collection.command.source`。命令意图路由的 steady state 集合锚与 cast dispatch cycle 的 group key 同样从实体侧派生（挂载上下文 → 载体实体；steady state → 保留 0）。
- 与表现层（presenter）的边界：interaction context 是纯交互域状态，**永不触碰 presentation 域**——不建/不收 presenter scope、不发 presenter 命令、不给 presenter 分配任何标识。表现层通过两种既有通道观察 context：(1) 一次性通知用 `ContextActivated/ContextDeactivated` 事件（presenter 规则可订阅，事件只携带 context profile id 与 parent profile id）；(2) 持续性/可存盘的装饰用 presenter 侧 `InteractionContextBinding` 行为（作者在 presenter 定义声明 `contextProfileId`，运行时按 owner 实体挂载的 `InteractionContextInstances` 快照消解写 param，消费方如 ScreenRect 的 `visibilityParamKey` 决定显隐）——恢复 / 热插拔后从实体状态自然正确，不依赖事件广播。
- 根资产 `assets/Input/interaction_context_profiles.json` 现为空表（`profiles: []`）；保留默认档案由 GameEngine 程序化安装（mod 的 DeepObject 合并会整体替换 profiles 数组，保留档案不能走根资产）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/InteractionContextProfile.cs |
| 档案注册（安装期 id 解析、挂载物化、steady state 锚） | src/Core/Input/Interaction/InteractionContextProfileRegistry.cs |
| 实体交互状态（完整帧读面） | src/Core/Input/Interaction/InteractionContextInstance.cs |
| 挂载/回收系统（类注释） | src/Core/Input/Interaction/AbilityExecInteractionContextSystem.cs |
| 执行期档案缺失报错 | AbilityExecInteractionContextSystem.cs（TrackStartedExecContexts） |
| 能力侧声明解析 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs |
| 意图解析 | src/Core/Input/Interaction/CommandIntentArbiter.cs |
| 集合提交（过滤 + 域路由） | src/Core/Input/Interaction/ContextBoundCollectionWriter.cs |
| 命令意图路由（集合锚 + cycle group key） | src/Core/Input/Orders/InputOrderMappingSystem.cs（SubmitCommandIntentOrder） |
| 上下文 → IMC 投影 | src/Core/Input/Systems/InputContextProjectionSystem.cs |
| pushFrame/popFrame op（实体化内核） | src/Core/Input/Interaction/CastCommitProfileRegistry.cs |
| 根资产 | assets/Input/interaction_context_profiles.json |

## 3. 栈退役路线图（#1306 路线④）——已完成

`InteractionContextStack` 已按 #1306 目标模型分六步退役完毕（#1351 投影吸收帧→IMC 翻译；#1366 实体读面先行；本页所在切片完成剩余）：

1. ~~帧状态实体化~~：帧携带的 contextId / activeCollectionKey / filterProfileId / commandIntentId / contextEntity 全部实体化为 `InteractionContextInstance` 稀疏组件字段；`AbilityExecInteractionContextSystem` 直写组件（每域 LIFO 仲裁，死亡载体在回收前的窗口内保持挂载，读侧 fail closed）。写侧与读侧同一组件，无平行真相。
2. ~~消费方迁移到实体读取~~：DEC-14 仲裁链、命令源 owner 解析、`ContextBoundCollectionWriter` 的过滤与集合路由、`InputOrderMappingSystem` 的集合锚与 cycle group key、interaction 面板 owner+集合键配对读全部改读组件；`OwnerToken` 语义由「载体实体派生 group key + steady state 保留 0」接替。
3. ~~注册表搬迁~~：五个共享 id 注册表全部离开栈本体——ContextId / InputContextId 归 `InteractionContextProfileRegistry`（安装期解析，InputContextId 空间为该注册表自有）；FilterProfileId / CommandIntentProfileId 归各自 kernel 注册表（安装期 fail-fast）；集合键本就归 `EntityCollectionStore`；EntityViewKey 无运行时消费方，id 空间删除（档案字段保留为声明数据）。
4. ~~CastCommitProfile pushFrame/popFrame 实体化~~：op 直接写 `InteractionContextInstance`（挂载源标记 CastCommitOp）；popFrame 只回收本源挂载，弹空或弹到 exec 挂载即配置错误 fail-fast。该内核仍无生产驱动，接线随未来 cast commit 消费方。
5. ~~默认帧退役~~：引擎启动压入的保留帧删除；steady state = 组件缺席，集合路由锚改由保留默认档案（`interaction.context.default`）承担，GameEngine 程序化安装。
6. ~~栈与残留机制删除~~：`InteractionContextStack` / `InteractionContextFrame` / `InteractionContextFrameDescriptor` / `OwnerToken` / `CoreServiceKeys.InteractionContextStack` 删除；superweapon showcase 与 interaction 面板的帧身份检查全部改读实体状态。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [input-01 reference](input-01-command-intent.md)
