# 指令系统代码对齐笔记

这份文档是 `commandSystem.md` 的配套笔记，目标不是定义最终方案，而是把当前仓库里已经存在的基建、现有命名对应物、以及真正还没收束的缺口拆开写清楚。

结论先写在前面：

- 指令系统不是从零开始，运行时主干已经很完整。
- 当前最大的风险不是“缺功能”，而是“设计文档把多个现有对象又抽象成了更高一层概念，但没有把现状层和目标层分开写”。
- 后续主文档建议同时维护两套术语：
  - `现状术语`：描述今天代码里已经稳定存在的对象、服务和运行时边界。
  - `目标术语`：描述希望未来收束成的一等抽象和统一模型。

## 已存在的主干

当前代码里已经存在一条可工作的指令系统主链路，可以粗分成四层。

### 1. 输入与上下文层

- `src/Core/Input/Runtime/PlayerInputHandler.cs`
- `src/Core/Input/Interaction/InteractionContextStack.cs`
- `src/Core/Input/Interaction/ControlSchemeRuntime.cs`
- `src/Core/Input/Interaction/CommandIntentArbiter.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`

这一层已经解决了这些事情：

- 设备输入被归一成 `InputAction`
- 交互上下文和输入上下文被拆开建模
- 控制方案可以热切换
- 指令意图可以从当前上下文中仲裁出来
- 输入最终可以映射成 order、cast、fanout submission

它离“完整终态”还有距离，但绝不是空白。

### 2. Client / Seat / Present 层

- `src/Core/Client/ClientLocalSeatRegistry.cs`
- `src/Core/Client/LogicViewRegistry.cs`
- `src/Core/Client/PresentBinding.cs`
- `src/Core/Client/PresentBindingPresentation.cs`
- `src/Core/Client/ClientLocalSeatAccess.cs`
- `src/Core/Client/ClientLocalSeatBindings.cs`

这一层已经形成了稳定链路：

- `Seat` 负责本地席位和 possession
- `LogicViewRegistry` 负责逻辑视角入口
- `PresentBinding` 负责把某个逻辑视角绑定到宿主表面矩形
- `PresentBindingPresentation` 负责把 presenter / picking / culling 和 present binding 同步起来

所以更准确的描述是：

- 当前并不是没有 `Client` 相关基建
- 而是 `Client` 这个更高层抽象还没有被收束成单一对象，现状是由一组 service contract 共同承担

### 3. 控制域与集合层

- `src/Core/Association/OwnershipResolver.cs`
- `src/Core/Gameplay/Relationships/ControlDomainQuery.cs`
- `src/Core/EntityCollections/ControlPlaneView.cs`
- `src/Core/Gameplay/Teams/ParticipantBindingResolver.cs`
- `src/Core/EntityCollections/EntityCollectionStore.cs`

这一层已经完成了：

- owns / controls / domain 的运行时查询
- 从 representative 出发聚合可控实体集合
- 部分控制域与完全控制域的区分
- map 启动时 participant、seat、logic view、control-plane 相关物化

也就是说，`Control Plane` 今天已经不是概念稿，而是有正式 runtime 的。

### 4. Knowledge 与 UI 承载层

- `src/Core/Knowledge/KnowledgeProjectionStore.cs`
- `src/Core/Knowledge/KnowledgeProjectionResolver.cs`
- `src/Core/Vision/FogKnowledgeProjector.cs`
- `src/Core/UI/PanelHosting/PanelHost.cs`
- `src/Core/UI/CommandDeck/CommandDeckProjector.cs`
- `src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs`

这一层已经完成了：

- viewer-target 认知投影存储与读取
- 认知可见性参与目标判定与 UI 可读性
- panel 生命周期管理
- graph op 到 panel host 的正式接入
- command deck / aggregation 类 UI 的运行时载体

所以 `KnowledgeProjection` 不该再留空，它已经是明确存在的正式概念。

## 引擎总装配

`src/Core/Engine/GameEngine.cs` 已经把上述对象以 service 的形式统一注册进引擎。

尤其关键的注册包括：

- `PanelHost`
- `ClientLocalSeatRegistry`
- `LogicViewRegistry`
- `ControlPlaneView`
- `KnowledgeProjectionStore`
- `KnowledgeProjectionResolver`
- `InteractionContextStack`
- `ControlSchemeRuntime`
- `CommandIntentProfileRegistry`
- `CastDispatchProfileRegistry`
- `OwnershipResolver`
- `ControlDomainQuery`

这说明当前仓库并不是“各自散着实现了一些点状能力”，而是已经有一条相对完整的基础设施装配线。

## 术语对应

下面这张表专门回答一个问题：`commandSystem.md` 里后半段新增术语，哪些在现有代码里已经有等价实现。

| 目标术语 | 当前代码中的对应物 | 说明 |
| --- | --- | --- |
| `EntityAbilitySet` | `AbilityStateBuffer`、`AbilityFormSetRegistry` | 实体具备哪些 ability、哪些 form，现状已经有承载结构 |
| `EntityAbilitySlot` | `AbilitySlotResolver`、`AbilityStateBuffer`、CommandDeck slot 路由 | 现有代码已经有 slot 解析能力，只是表达方式还不是文档里的“语义 key 一等抽象” |
| `CommandPref` | `ClientCastPreferenceStore`、`CastCommitProfileRegistry`、`ControlSchemeRuntime` 持久偏好 | 玩家施法偏好、提交偏好、控制方案偏好并不是空白能力 |
| `CommandContext` | `InteractionContextFrame`、`InputOrderActivationContext`、`CommandIntentTargetFacts`、`InputOrderMappingSystem` 内部上下文 | 现状已经有上下文信息，但还没有被收束成单一 DTO |
| `CommandIntent` | `CommandIntentProfileRegistry`、`CommandIntentArbiter`、`InputOrderMappingSystem` | 意图层实际已经存在 |
| `OrderFanout` | `ICommandActorExpander`、batch submit、cluster submit、`ControlPlaneView` | 一个输入扇出多个执行实体，这条线今天已存在 |
| `AggregationPanel` | `CommandDeckProjector`、`AbilityAggregationProfileRegistry`、`PanelHost` | 聚合展示层已有能力，只是命名和文档不同 |

## 这次容易误判的点

下面这些点最容易在第一次阅读时被误判成“没做”。

### 1. `LogicView` 不是没做，而是语义范围还没收束

当前 `LogicViewRegistry` 已经是正式入口，但它承载的更像：

- owner rep
- camera
- present binding 关联入口

而你在设计文档里期待的 `LogicView` 更大，里面还想包含：

- 可见对象集合
- 持久认知状态
- bot 决策输入
- 回放友好的快照

所以这里的问题不是“没有 LogicView”，而是“当前 LogicView 只承载了其中一部分职责，其他职责还分散在别处”。

### 2. `UISurface` 不是没做，而是由多块系统拼起来了

当前至少有这几块已经存在：

- `UiSurfaceHost` 作为宿主侧接缝
- `PanelHost` 作为面板生命周期与变量刷新核心
- `PresentBinding` 负责屏幕矩形绑定
- command panel / command deck / panel activation 等周边服务

所以更准确的说法是：

- 还没有单一名为 `UISurface` 的总对象
- 但 UI 承载相关能力已经形成系统

### 3. `Viewport` 也不是没做，而是现状被拆成 binding + host view + camera pipeline

当前视口相关职责分散在：

- `PresentBinding`
- `IViewController`
- `PresentBindingPresentation`
- presenter / ray provider / culling

缺的是把这些职责再包成一个统一 viewport 实例。

### 4. `Control Plane` 已经是 runtime，不应再用“未来式”整体描述

当前的 `ControlDomainQuery + ControlPlaneView + CommandDeckProjector` 已经足够说明：

- control plane 不是纸面概念
- 但它还没达到你要的“完全数据驱动 query graph 终局”

所以主文档后续应该拆成：

- `当前 control-plane runtime`
- `未来 control-plane authoring/runtime 收束目标`

## 真正还没收束的缺口

经过这轮对齐，真正应该继续设计的缺口主要有下面几类。

### 1. 统一一等抽象还没封口

主要包括：

- `Client`
- `App`
- `Device`
- `UISurface`
- `Viewport`
- `EntityAssociation`

这些概念今天大多有能力基底，但还没有统一顶层对象。

### 2. `InteractionContext` 现状是 stack-first，不是 graph-first

`InteractionContextStack` 已经很强：

- 可 push
- 可按 token 删除
- 可按 context entity 回收

但它仍然更接近“增强栈”，不是“显式图模型”。

### 3. `Control Plane` 还没完全配置化

当前 control-plane 运行时已经很扎实，但仍然主要由 C# runtime 组合完成。

文档目标里强调的：

- query graph 明文配置
- 过滤 / 排序 / 扇出规则数据驱动

这部分仍是未来工作。

### 4. `CommandContext` 还没有统一、可序列化的外露模型

今天上下文数据真实存在，但散落在：

- interaction frame
- cast preference
- command intent profile
- activation context
- target facts
- input mapping system 内部状态

如果后面要做：

- 明确调试
- 确定性回放
- 指令审计
- AI / bot 接口

那么把它收束成一个统一 `CommandContext` 模型会很重要。

## 对 `commandSystem.md` 的改写建议

后续主文档建议按下面结构重写。

### 第一层：现状模型

只写今天仓库已经存在、已经能跑的对象和边界：

- Seat
- LogicViewRegistry
- PresentBinding
- InteractionContextStack
- ControlSchemeRuntime
- CommandIntent
- InputOrderMapping
- ControlPlaneView
- KnowledgeProjection
- PanelHost / CommandDeck

### 第二层：目标模型

单独写未来希望收束出的高层抽象：

- Client
- App
- Device
- UISurface
- Viewport
- LogicView 终态
- CommandContext 终态
- Control Plane 配置化终态

### 第三层：从现状到目标的迁移规则

重点回答：

- 哪些现有 service 要继续保留
- 哪些概念只是重命名
- 哪些对象需要真正新建
- 哪些数据必须配置化

## 一句话收束

这套基建当前最真实的状态是：

**运行时基础设施已经相当完整，但设计文档还没有把“现状实现”与“目标抽象”剥离开。**

所以接下来最值得做的，不是从头发明一套新的指令系统，而是把现有骨架提纯、命名、收束成你真正想要的那层模型。
