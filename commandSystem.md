# 指令系统

## 设计目标

设计一套可以搞定所有游戏品类技能施法逻辑的系统

## 结构概念

我们的ludots的核心是，做了如下分层术语：

- Machine（原稿称 Client，2026-08 已更名）：实体的或虚拟机的机器
  - 更名原因：术语治理定案（#902 §3.5，见 `gitbook/architecture/terminology.md`）——「client」一词只保留 client-local（本机）与 ReplicatedClient（网络角色）两个义，禁止再指机器
- App：UE、Raylib等进程宿主
- Device：Seat 接入的控制器设备，如键鼠、触控、手柄等
- 以上所有的实体设备，都可以通过Agent Bridge来Mock模拟
- Input Action Mapping：配置Device输入映射为具体的什么输入action语义，例如空格是跳跃，B键可能也是跳跃，一个触控的向上手势Gesture，可能也是跳跃，这里会把设备参数灵敏度、按压时间，都解释为具体的初始语义
- Input Context：一个特定的输入上下文，例如我在设置菜单有一套快捷键，在外交系统又有一套快捷键，不同的input context下，同一个设备输入可能有不同含义；甚至可以有多个同时作用的inputcontext，例如开着车丢香蕉皮，在一些游戏里还是很常见的，没有任何要求input context必须是唯一的，操作互斥是作者的约定处理好的，我们作为编辑器只提供警示
- Input Context Stack/Graph：input context可能有一套栈式或图式的结构，上一层操作完毕后，按条件配置到之前的层或指定层，都是有可能的，需要明文配置
- Seat：一个App进程接入的玩家席位
- LogicView：ludots架构特有的，一个纯粹逻辑的，描述一个当前Entity“看到的”的对象合集和其他信息，这个信息是会持久化的，一是方便模拟bot，二是可以做确定性回放
- CameraPresenter：将LogicView中的对象，解析为Presentation请求，以及确认“相机内”的Presenter对象，和相机外的预热、LOD区域
- UISurface：将不同渲染后端、DOMTree后端的UI元素集合
- Viewport：特定玩家席位分配看到的视口，由LogicView经由CamearaPresenter渲染（渲染由宿主负责），和UISurface（渲染由宿主负责）最终合成的画面
- Representative Entity：在ludots世界里，player也好、team也好，都没有“特殊地位”，全部都是物化为实体的，所以比如player要走路、有look，那一定都计入在entity上面，允许写一些语法糖性质的playermanger或teammanager，但player的数据、关系，都必须落在entity上，这些manager只是拿到数据投影，这样可以维持SSOT
- Control Plane：我作为一个玩家，可以控制哪些Entity，无论是FPS、TPS、RTS也好，我的3c控制一定是落在一个角色player上的，无论它有没有外观皮肤，那么这个player其实就是第一个控制的Entity，这个入口是唯一的，一般来说，ControlPlane就指的是，从一个唯一入口Entity（一般就是Representative），通过一个query graph（查询规则函数），进行一系列过滤、排序，得到这个entity具备控制权的所有Entity集合，这个query必须是数据驱动走明文配置的，目前可能不是这样，但未来必须如此。
- EntityAssociation：实体关联能力——现有 Association 子域（OwnershipResolver、ScopeKey 等）解决 ownership 与关联查询；作为整体规划的 Entity Association Core（计划与 ADR 的 SSOT：issue #239）是更大的未来核心能力
- KnowledgeProjection：viewer-target 认知投影——记录「谁看见/知道谁什么」，已落地为正式 runtime（`src/Core/Knowledge/KnowledgeProjectionStore.cs`），迷雾经 FogKnowledgeProjector 写入
- EntityAbilitySet：一个实体的技能组，代表它具备施法这些ability的能力
- EntityAbilitySlot：一个实体的技能“字典”，因为技能template有很多不同的id，一个语义化的key映射到具体id是很重要的，例如sc2里，陆战队有陆战队的普攻、坦克有坦克的，当你框选他们的时候，右键下单攻击，走的都是attack这个语义；在LOL中，QWER看似是快捷键，实际上代表了技能123和大招，这里容易有个陷阱就是认为abilitySlot和快捷键是一回事，实际上在ludots不是；在LOL手游里，这个就很明显，你只会看到技能1、技能2、技能3的button和大招的button
- EntityCollectionStore：代表一个玩家目前的一套字典化（但soa）的实体集合表，可以自己声明key和value，例如我的编队1、我的当前“选中”
- CommandPref：一些玩家的偏好数据配置，例如施法模式（按下还是抬起，是否需要二次确认，等等，例如LOL的智能施法就是一种），这种数据可能是Per Ability Template的，可能是Per Player、Per Game Instance的，总之不会内建在具体的ability配置里面，如果有，也是通过meta数据的形式保存
- InteractionContext：玩家需要“下令”施法时候的数据状态，一般就是CommandPref、当前InputContext、EntityCollection中的几个特定集合、当前Representative Entity或指定集合entity的attribute或状态、下达的Input Action和特定的设备参数路由转化，组成了一次指令下单的所有必要数据；例如蜘蛛侠、蝙蝠侠里，你按Action1，可能根据周围的环境、敌人威胁，一个button路由到不同技能；例如皇室战争，你的第一个牌下去可能是不同的施法方式和技能，这个就是abilitySlot；所以intent和最终施法的ability order是两回事，中间可能经过了多重规则的映射、跳转、参数透传
- CommandIntent：一次施法意图，由CommandContext最终抛出
- OrderFanout：施法意图最终扇出的实际ability指令，例如你框了一大群人，或者在新战神里由老父亲命令阿特柔斯行动，这些都是同一个command input扇出的额外指令
- AggregationPanel：就像刚才说的，施法意图和施法快捷键、施法语义，是三回事情，例如新版LOL潘森，你1技能是一个长矛，如果你短按，就是瞬间伤害，长按会进入蓄力，长按抬起是投掷投射物，长按超时就是取消施法，这实际上是四个order，四种意图，一个panel的显示和快捷键。这里还有很多细节。

所以一次交互里需要关注的情况是：

- 我控制了哪些实体
  - 这个应该是EntityCollectionStore里的一个指定key的集合
  - 这个集合应该是Graph配出来
  - 配置方式，应该是经由trigger（也是一个带副作用的graph，或代码）或者system代码，设置到Store里的
- 实体本身的上下文：
  - 实体本身的属性和状态
  - 实体关联的数据和其他实体的属性和状态
  - 以上这些应该是一个graph配置的
- 我的当前的InputContext
- 我的一些中间步骤操作

    - 有一次性的
    - 有持续性的
    - 操作的参数都会写入一个上下文
    - 中间步骤的操作可能衍生出新的context
    - 中间步骤的操作可能扇出更多意图
    - 中间步骤的操作可能可以回退
  - 我最终提交意图的操作
- 用户是可以切换输入设备和输入偏好的，这个目前好像是control scheme
- 不同游戏的中间数据哲学可能大相径庭，但是总体结构上就是这些

## 当前理解与代码现状差异

下面这张表是基于当前仓库代码做的第一次对照，目的是把“概念是否已经落地”说清楚，避免后面设计时把已有能力、局部能力、未来目标混在一起。

| 概念                      | 我当前文档里的理解                                                                  | 代码现状                                                                                                                                                                                                              | 差异判断                                                                                            | 证据                                                                                                                                                                                      |
| ------------------------- | ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Client                    | 一个客户端，可以是真实机器，也可以是虚拟机/Agent Bridge 模拟端                      | 代码里没有一个统一的`Client` 核心对象，当前更接近“本地席位 + 逻辑视图 + 宿主视图”的组合；客户端侧核心入口主要是 `ClientLocalSeatRegistry`、`LogicViewRegistry`、`PresentBinding`                            | 设计抽象高于当前实现；目前更像一组组合件，不是单一 SSOT 类型                                        | `src/Core/Client/ClientLocalSeatRegistry.cs` `src/Core/Client/LogicViewRegistry.cs` `src/Core/Client/PresentBinding.cs`                                                             |
| App                       | UE、Raylib 等宿主进程                                                               | 宿主侧能力存在，但以 bootstrap / host service / view controller 的方式分散存在，没有看到统一的`App` 域模型                                                                                                          | 这个概念在设计里成立，但代码里还是“宿主适配接口集合”，不是统一对象模型                            | `src/Core/Hosting/GameBootstrapper.cs` `src/Core/Presentation/Camera/IViewController.cs` `src/Core/Scripting/CoreServiceKeys.cs`                                                    |
| Device                    | 键鼠、触控、手柄等接入设备，且都可被 Mock                                           | 设备层目前有两层：一层是语义输入`IInputActionReader` / `PlayerInputHandler`，另一层是宿主事件级的 `SyntheticInputDevice`。Mock 能力已存在，但没有看到统一的设备注册/拓扑模型                                    | “可 Mock” 已落地，但“Device 是正式一等抽象”这件事还没完全落地                                   | `src/Core/Input/Runtime/IInputActionReader.cs` `src/Core/Input/Runtime/PlayerInputHandler.cs` `src/Core/Input/Runtime/SyntheticInputDevice.cs`                                      |
| Input Action Mapping      | 设备输入先映射成 action 语义                                                        | 这部分是实的。`InputConfig` 定义 action，`PlayerInputHandler` 维护 action 状态，`InputOrderMappingSystem` 继续把 action 映射成 order / cast / command intent                                                    | 基本成立，但当前重点是“输入语义 -> 指令/订单”，不是“设备抽象 -> 统一动作语义”的完整全链路建模   | `src/Core/Input/Config/InputConfigModels.cs` `src/Core/Input/Runtime/PlayerInputHandler.cs` `src/Core/Input/Orders/InputOrderMappingSystem.cs`                                      |
| Input Context             | 同一输入在不同上下文含义不同，可叠加                                                | 这部分已经有明确实现。`PlayerInputHandler` 支持上下文切换；`InteractionContextStack` 的 frame 可携带 `InputContextId`；`InputContextProjectionSystem` 每 tick 把帧需求和实体交互模式投影成输入上下文 push/pop（#1306 分层：模式是模拟状态，输入上下文是本机投影）       | 概念成立，且代码已经把“交互上下文”和“输入上下文”明确拆开了                                      | `src/Core/Input/Interaction/InteractionContextStack.cs` `src/Core/Input/Systems/InputContextProjectionSystem.cs`                                                          |
| Input Context Stack/Graph | 可能是栈，也可能是图，可显式返回上一层或指定层                                      | 当前明确实现的是 stack，不是 graph。支持 push、按 token 移除、按 context entity 回收，但没有看到“图式跳转 / 返回指定节点”的统一机制                                                                                 | 文档现在把未来目标和现状写在了一起；现状应先写成 stack-first                                        | `src/Core/Input/Interaction/InteractionContextStack.cs`                                                                                                                                 |
| Seat                      | 一个 App 进程接入的玩家席位                                                         | 这部分已经很明确。`ClientLocalSeatRegistry` 管理 seat，seat 记录 `ControlSchemeId`、`PossessedRep`、`PresentBinding`；`ParticipantBindingResolver` 在 map/launch 绑定阶段发布 local seats                   | 基本成立，且已经是核心术语                                                                          | `src/Core/Client/ClientLocalSeatRegistry.cs` `src/Core/Gameplay/Teams/ParticipantBindingResolver.cs`                                                                                  |
| LogicView                 | 一个 Entity “看到的”对象集合和其他信息，并且要持久化，支持 bot 和确定性回放       | 当前代码里的`LogicView` 更偏“逻辑视角/相机注册表”，核心内容是 owner rep + camera；还没看到一个统一的“可见对象集合 + 认知结果 + 持久化快照”总对象                                                                | 这里是当前文档和代码差异最大的点之一：名字已经有了，但语义范围比文档里窄很多                        | `src/Core/Client/LogicViewRegistry.cs` `src/Core/Systems/CameraRuntimeSystem.cs`                                                                                                      |
| CameraPresenter           | 把 LogicView 解析成 presentation 请求，并处理相机内/外对象、预热与 LOD              | 当前有`CameraPresenter`、`CoreScreenProjector`、`CameraCullingSystem`，但它们更偏“相机状态到屏幕投影/裁剪”的表现层管线，不等于文档里那个广义的 LogicView 解释器                                               | 已有较强实现基础，但职责边界比文档设想更窄、更偏渲染/投影                                           | `src/Core/Presentation/Camera/CameraPresenter.cs` `src/Core/Presentation/Camera/CoreScreenProjector.cs` `src/Core/Systems/CameraCullingSystem.cs`                                   |
| UISurface                 | 不同渲染后端、DOMTree 后端的 UI 元素集合                                            | 当前没有统一名为`UISurface` 的核心类型。已有的是 `PanelHost`、Panel Projection、Graph `CreatePanel`/`DestroyPanel`、以及 `UiSurfaceHost` service key 这类宿主侧接缝                                         | 说明 UI 承载能力在做，但“统一 UI Surface 模型”还没有完整收束成一个一等对象                        | `src/Core/UI/PanelHosting/PanelHost.cs` `src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs` `src/Core/Scripting/CoreServiceKeys.cs`                                               |
| Viewport                  | Seat 视口，由 LogicView + UI 合成最终画面                                           | 当前最接近这个概念的是`PresentBinding` + `IViewController`。`PresentBinding` 规定某个 LogicView 画到宿主表面的哪个矩形区域，`PresentBindingPresentation` 负责同步 presenter / picking / culling               | 概念大体成立，但现状更偏“present binding 视口绑定”，还不是一个统一的 viewport 组合对象            | `src/Core/Client/PresentBinding.cs` `src/Core/Client/PresentBindingPresentation.cs` `src/Core/Presentation/Camera/IViewController.cs`                                               |
| Representative Entity     | player、team 等都必须落在实体上，manager 只是投影                                   | 这条是实的。`ParticipantBindingResolver` 明确要求 team/player 都通过代表实体 instance id 绑定到 ECS 实体上，再给实体写入身份组件                                                                                    | 这部分和代码是一致的，甚至已经是当前架构约束之一                                                    | `src/Core/Gameplay/Teams/ParticipantBindingResolver.cs` `src/Core/Systems/MapLoadEntityIndex.cs`                                                                                     |
| Control Plane             | 从唯一入口 representative 出发，通过 query graph 得到可控实体集合，最终必须数据驱动 | 这部分已经有明确实现雏形，而且不只是概念：有控制域拓扑、控制集合视图、命令面板投影、showcase 和测试。但当前核心仍偏 query/runtime 组合，不是“完全数据驱动 query graph”终态                                          | 可以视为“已经落地，但还没完全达到文档里设想的配置化终局”                                          | `src/Core/EntityCollections/ControlPlaneView.cs` `src/Core/UI/CommandDeck/CommandDeckProjector.cs` `src/Tests/GasTests/Production/ControlPlaneProjectionShowcaseAcceptanceTests.cs` |
| EntityAssociation         | 目前文档里还没定义清楚                                                              | 当前代码里有`Association` 子域，例如 `OwnershipResolver`、`ScopeKey`、`EntityKeyedSoaTable`，但没有看到一个叫 `EntityAssociation` 的统一总对象；同时仓库规则和 issue 语境里又把它当成一个更大的未来核心能力 | 这里建议先补定义，不然很容易和“现有 Association 子域能力”以及“未来 Entity Association Core”混用 | `src/Core/Association/OwnershipResolver.cs` `src/Core/Association/ScopeKey.cs` `src/Core/Association/EntityKeyedSoaTable.cs`                                                        |
| KnowledgeProjection       | 我暂时忘了                                                                          | 这部分其实已经非常明确：`KnowledgeProjectionStore` 以 viewer-target 形式存储认知投影，`FogKnowledgeProjector` 写入，`KnowledgeProjectionConsumer/Resolver` 读取，并参与 UI / 目标判定 / 可读性约束              | 这是已经落地且值得反向提升到设计文档里的核心概念，不应继续空着                                      | `src/Core/Knowledge/KnowledgeProjectionStore.cs` `src/Core/Vision/FogKnowledgeProjector.cs` `src/Core/Knowledge/KnowledgeProjectionConsumer.cs`                                     |

### 初步结论

当前这份文档里的很多术语已经能对上代码，但它们分成了三类：

1. 已经有稳定实现并且术语基本对齐：`Seat`、`Input Context`、`Representative Entity`、`KnowledgeProjection`。
2. 已经有实现基础，但文档语义比代码更大：`LogicView`、`CameraPresenter`、`Viewport`、`Control Plane`。
3. 设计语言已经提出，但代码里还没有统一一等抽象，或者仍然分散在多个能力里：`Client`、`App`、`Device`、`UISurface`、`EntityAssociation`、`Input Context Graph`。

如果后面继续写“指令系统”，我建议先把这些概念分层成“现状术语”和“目标术语”两套，不然很容易在设计里默认某些能力已经存在统一入口，但代码实际还没有。
