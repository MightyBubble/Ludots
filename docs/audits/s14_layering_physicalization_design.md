# S14 分层物理化设计（第一阶段 · 只出设计）

**状态：** 设计提案。评审通过之前不搬生产代码、不改 csproj、不把 gitbook 合同改成「已落地」。
**基线：** `origin/main` @ `82ddb3322a`
**计划正本：** GitHub PR #942 计划文 §S14（该文尚未合入本基线，以 PR head 为准）
**审查编号：** B21、B22、B23、B24、B25、C21、C22
**前序（不推翻）：** S8（#951）顺序守卫临时加固；S10（#952）跨层组件 write owner 文档裁决
**本文角色：** `docs/audits/` 审计/提案，不是正式架构门户。正式层合同仍以 [图分层](../../gitbook/architecture/graph-layering-flow-and-behavior.md)、[GAS 分层](../../gitbook/architecture/gas-layered-architecture.md)、[实体仿真分层](../../gitbook/architecture/entity-simulation-layering.md) 为准。

---

## 1 概述

今天层与层之间的墙只写在文档和文本扫描里。`src/Core/Ludots.Core.csproj` 一个程序集同时装下 L0 虚拟机、L1 编译器、L2 行为调度、GAS、Presentation、Input、Spatial、Navigation。Mod 通过 `ScriptContext.GetEngine()` 拿到具体引擎，再直接登记系统、改进程级静态表。编译器看不见这些越界。

S14 的目标是让墙在编译期存在：程序集引用图、类型放在哪、分析器三条一起守。本阶段只定切法、合同和可分批验证的迁移路径。不搬文件。

六个问题的结论（正文在 §3）：

1. **程序集：** 4 个 abstractions-only 契约程序集 + 7 个实现程序集 + 现有 Physics2D 三件套与 `Ludots.Platform.Abstractions`。宿主留下 `Ludots.Engine`。Navigation / Vision / Persistence / Association 本阶段不另切。
2. **注册表：** 进程级静态表收成引擎上的 `ModRegistrySet` 实例。生产 API 删除 `Clear()`；测试换新实例。`Freeze()` 是该实例上的单向转换，禁止解冻。
3. **Mod 可见面：** `IModContext` 成为 Mod 编译期唯一通路。`GetEngine()` 先标过时再按访问模式分批拆，最后删除。基线上 `.GetEngine(` 调用 205 次（mods 203 + src 2），定义 1 处。
4. **跨层组件：** 不改 S10 的 owner 裁决。模拟侧拥有的身份/销毁标记进契约程序集；表现侧拥有的 `CullState` / `VisualTransform` 进 Presentation 实现程序集。本阶段不做 partial-world。
5. **SystemGroup：** `enum SystemGroup` 的声明顺序是唯一数据源。运行时用 `Enum.GetValues<SystemGroup>()`。删掉 `PhaseOrder` 数组副本。S8 的按序交叉校验在根治后变成「运行时表就是枚举本身」。
6. **迁移：** 六波，每波可独立合入、可回退检测。先在同一程序集里把合同和棘轮立住，再一次剥一层。禁止「一周全切完」。

---

## 2 结构

```text
契约（abstractions-only，无实现、无 ECS system）
  Ludots.Contracts
  Ludots.Graph.Abstractions
  Ludots.GAS.Abstractions
  Ludots.Modding.Abstractions

实现（只依赖自己该看见的契约）
  Ludots.Graph.Runtime          L0：指令执行、handler 调度、登记表实现
  Ludots.Graph.Authoring        L1：前门 + 控制流编译器 + 作者文档
  Ludots.Graph.Behavior         L2：行为树 / HFSM / 关卡导演
  Ludots.GAS                    效果、属性、标签、模板、技能定义、运行时 API 实现
  Ludots.Spatial                空间查询与分区
  Ludots.Input                  输入收集与命令源
  Ludots.Presentation           表现循环、裁剪、插值、HUD、Tag 显示表内容

宿主
  Ludots.Engine                 GameEngine、ModLoader、本阶段未剥走的其余 Core
  Ludots.Physics2D + Broadphase + Movement.Physics2DBridge   已拆，保持
  Ludots.Platform.Abstractions  已有

Mod 只引用
  Ludots.Modding.Abstractions + 它声明要用的契约
  不得引用 Ludots.Engine、不得看见 GameEngine
```

依赖方向（编译期墙）：

```text
Modding.Abstractions ──► Contracts
Graph.Abstractions   ──► Contracts
GAS.Abstractions     ──► Contracts

Graph.Runtime   ──► Graph.Abstractions, Contracts
Graph.Authoring ──► Graph.Abstractions, Contracts          （禁止 Presentation）
Graph.Behavior  ──► Graph.Abstractions, Contracts          （禁止 Authoring 实现）
GAS             ──► GAS.Abstractions, Graph.Abstractions, Contracts, Spatial
Spatial         ──► Contracts                              （禁止 Presentation）
Input           ──► Contracts, GAS.Abstractions            （禁止 Presentation）
Presentation    ──► Contracts, GAS.Abstractions            （可读模拟身份；禁止写）
Engine          ──► 上述全部（唯一组合根）
```

禁止的边：L0 ↛ GAS / Presentation；L1 ↛ Presentation；Input / GAS / L2 ↛ Presentation；Mod ↛ Engine。Physics2D 今天仍引用整颗 `Ludots.Core`，那是「文件拆了、墙没立」。剥层时不准再走这条路。

```text
1 概述
2 结构（本页）
3 详情：六个问题、现状计数、CI
4 场景：维护者与作者会撞到什么
5 边界
6 UAT
附录 A 计数命令
```

---

## 3 详情

### 3.1 切几个程序集、边界画在哪

#### 3.1.1 契约程序集（abstractions-only）

只放接口、枚举、只读 DTO、owner 标记、模拟侧拥有的跨层组件。不放 system、不放 handler、不放静态可变表。

| 程序集 | 放什么 | 不放什么 |
|--------|--------|----------|
| `Ludots.Contracts` | `SystemGroup`、`SystemGroupOrder`、`[WriteOwner]` / `[ReadAllowed]`、`PresentationStableId`、`PresentationDestroyPending` | 任何 system；`CullState`；`VisualTransform` |
| `Ludots.Graph.Abstractions` | `GraphInstruction`、`GraphKind`、`GraphNodeOp`、`IGraphIdRegistry`、`IGraphProgramRegistry`、瘦身的 `IGraphRuntimeApi`（只谈实体/整数/空间结果，不谈具体 GAS 组件） | `GasGraphOpHandlerTable`；`GraphControlFlowCompiler`；`GraphControlFlowDocument` |
| `Ludots.GAS.Abstractions` | 属性/标签/效果/技能的 id 与登记表接口；`IDerivedAttributeGraphRuntimeApi` | Effect 管线实现；handler |
| `Ludots.Modding.Abstractions` | `IMod`、`IModContext`、`ISystemRegistrar`、`IRegistrySetView`、`ISpatialSession`、`IConfigView` | `GameEngine`；`ModContext` 实现；`GetEngine()` |

`IGraphRuntimeApi` 今天住在 `src/Core/NodeLibraries/GASGraph/IGraphRuntimeApi.cs`，using 已拉进 GAS 组件、关系、队伍、导航、空间。搬进契约时必须先把签名收成原语；派生属性写入接口留在 `Ludots.GAS.Abstractions`。

#### 3.1.2 实现程序集

**L0 `Ludots.Graph.Runtime`。** 指令缓冲、`GraphProgramRegistry` 实现、`Execute` / `ExecuteSlice` 调度循环、handler **槽位**。今天的 `GasGraphOpHandlerTable` 把调度和 GAS/Placement/Relationships/Teams 的 using 写在同一个类型里，且不在 `src/Core/GraphRuntime/` 守卫目录。切开之后：L0 只拥有表和循环；具体 handler 由 GAS（以及空间/关系）在引擎组合时登记。禁止再出现 `static readonly Instance` 在构造函数里直接绑定 GAS 类型。

**L1 `Ludots.Graph.Authoring`。** `GraphProgramAuthoringFrontDoor`、`GraphControlFlowCompiler`（含 Linear/Query 分部）、作者糖、`GraphControlFlowDocument`。文档今天放在 `src/Core/GraphRuntime/GraphControlFlowDocument.cs`，`using Ludots.Core.NodeLibraries.GASGraph` 取 `GraphOutputConfig`，把「GraphRuntime 不许引用 GAS」的文本守卫从旁边绕开（守卫只扫 `Gameplay.GAS` 字面量，扫不到 `GASGraph`）。作者文档属于 L1，必须离开 L0 目录。

L1 今天还 `using Ludots.Core.Presentation.TagDisplay`（`GraphControlFlowCompiler.Linear.cs` / `Query.cs`）。编译器只需要表 id 与选择策略枚举，不需要表现表内容。这些字段进 `Ludots.Graph.Abstractions`；表内容留在 Presentation。

**L2 `Ludots.Graph.Behavior`。** 现有挂靠点：`src/Core/Gameplay/AI/BehaviorTree/`、`src/Core/Gameplay/AI/Fsm/`、`src/Core/Gameplay/Level/LevelDirector.cs`。L2 只通过 L0 执行前门跑叶子图，不引用 L1 编译器实现。S9 的执行帧合同落地后，本程序集是它的物理边界。

**GAS `Ludots.GAS`。** `src/Core/Gameplay/GAS/` 主体：效果管线、属性缓冲、标签、模板、技能定义、以及 `IGraphRuntimeApi` 的实现。向 L0 登记自己的 opcode handler。

**Presentation `Ludots.Presentation`。** `src/Core/Presentation/`，外加今天误放在 `src/Core/Systems/CameraCullingSystem.cs` 的裁剪系统。`CullState`、`VisualTransform` 的类型定义必须在这个程序集里，这样 Input / GAS / L2 连类型名都写不出来。

**Input `Ludots.Input`。** `src/Core/Input/`。禁止引用 Presentation。选中与下单只看模拟状态（S10 已写）。

**Spatial `Ludots.Spatial`。** `src/Core/Spatial/`。禁止引用 Presentation。今天 `SpatialBoundsUtility` 读 `VisualTransform`——这是墙不存在的直接证据。剥层时这些读取必须改走 `WorldPositionCm` / `SpatialBounds`。不改 S10 的「表现不决定玩法」裁决。

**宿主 `Ludots.Engine`。** `GameEngine`、`ModLoader`、`ModContext` 实现、`PhaseOrderedCooperativeSimulation`、以及本阶段不剥的 Navigation、MassNavigation、Vision、Persistence、Association、Map、UI 投影宿主。它是唯一允许引用所有实现程序集的组合根。

#### 3.1.3 本阶段不切

- Navigation / MassNavigation / Vision / Persistence / Association / 多数 Map：仍在 Engine。再切是后续票。
- 八个 GraphOps 家族 Mod：不删、不退役（S6 锁门是另一票）。
- Physics2D 三件套：保持独立，但后续应改成引用契约/Spatial，而不是整颗 Core。
- 不新增 AAC 平行 ADR，不改 `docs/adr/`。

#### 3.1.4 过渡期门面

剥层期间允许暂时保留名为 `Ludots.Core` 的门面程序集，用 `TypeForwardedTo` 把旧命名空间指到新程序集，避免 167 个引用 `Ludots.Core.csproj` 的项目同一天全改。门面不得含新实现。某一层剥干净、棘轮把引用数压下去之后，再删门面。这是防大爆改的机械手段，不是长期架构。

---

### 3.2 注册表：从进程级静态变成实例状态

#### 3.2.1 现状（对照源码，不是记忆）

静态 `*Registry`（`public static class`）在 `src/Core` 里有 19 个，包括 `GraphIdRegistry`、`AttributeRegistry`、`TagRegistry`、`AbilityIdRegistry`、`EffectTemplateIdRegistry`、`ConfigKeyRegistry`、`LayerRegistry` 等。实例表同时存在：`GraphProgramRegistry`、`EffectTemplateRegistry`、`AbilityDefinitionRegistry`。

`Clear()` 与冻结的合同不统一（B25）：

| 行为 | 类型 |
|------|------|
| 冻结时 `Clear()` 抛错 | `AttributeRegistry`、`AttributeSinkRegistry` |
| `Clear()` 把 `_frozen = false`（静默解冻） | `GraphIdRegistry`、`TagRegistry`、`AbilityIdRegistry`、`EffectTemplateIdRegistry`、`ConfigKeyRegistry`、`LayerRegistry`、`AbilityFormSetIdRegistry`、`ContextGroupIdRegistry` |
| `Clear()` 不看冻结 | `UnitTypeRegistry`、`PerformerScopeTagRegistry`、`ProgressionIdRegistry`、`ProgressionRequirementIdRegistry` |
| 实例 `Clear()` 同时复位 finalized | `EffectTemplateRegistry`、`GraphProgramRegistry` |

`GraphIdRegistry.Clear()` 是 `public static`，连 `_frozen` 一起复位。生产调用：5 个 GraphOps bootstrap + `GraphProgramConfigLoader.LoadIdsAndCompile` 开头各 1 次。测试侧 58 次，工具 1 次。同进程二次装载时 name→id 与实例级 id→program 错位是必然的。

`TeamManager` 也是进程级静态。7 个 showcase 家族、8 个文件、17 次调用。

#### 3.2.2 目标合同

引擎（或即将创建的引擎）拥有一份 `ModRegistrySet`：

- 身份表：图名→id、属性、标签、效果模板 id、技能 id、配置键、层……
- 内容表：`GraphProgramRegistry`、`EffectTemplateRegistry`、`AbilityDefinitionRegistry` 等已是实例的表，挂进同一份 set，共享生命周期。
- Loader 的构造函数接收这套 set，不再碰静态类。

规则：

1. **没有 `Clear()`。** 生产 API 删除。要空表就 `new ModRegistrySet()`，交给新的引擎或新的装载会话。
2. **`Freeze()` 是该实例上的单向转换。** 冻结后再 `Register` 抛错。没有解冻。热改（Live Skill Workbench）继续走已经存在的 `GraphProgramRegistry.ReplaceProgram`：只换程序体，不准换 id、不准换 kind、不准复位冻结。身份重映射仍是 `EngineRestartRequired`。
3. **多地图 / 多引擎 / 热改的共同前置** 就是这一条：表跟实例走，不跟进程走。地图重载 = 新 set 或新引擎，不是把旧表洗白。
4. **测试** 用新实例，不再调用生产 `Clear()`。需要隔离的夹具标 `[NonParallelizable]` 的理由消失——因为不再有进程级可变表。

`GraphProgramConfigLoader` 今天一进 `LoadIdsAndCompile` 就 `GraphIdRegistry.Clear()` + `_registry.Clear()`。改成：要求传入的 set 仍未冻结且图身份表为空；不空则失败关闭，由调用方决定是换新 set 还是报「重复装载」。

#### 3.2.3 分批（仍在同一 `Ludots.Core` 里就能做）

1. 引入 `ModRegistrySet` 与接口，引擎持有一份；静态类内部改成转发到「当前引擎的 set」（临时，仅 Wave 2 前半）。
2. Loader / 测试改为显式注入。
3. 统一冻结：一律冻结后禁 `Clear`、禁解冻。
4. 删静态类与 `Clear()`。转发层不得残留。

不得在静态类上「加一把锁」假装实例化。

---

### 3.3 Mod 只能看见什么

#### 3.3.1 唯一通路

**是：`IModContext` 成为 Mod 编译期唯一通路。**

今天的 `IModContext`（`src/Core/Modding/IModContext.cs`）本身干净：`ModId`、`VFS`、`FunctionRegistry`、`SystemFactoryRegistry`、`TriggerDecorators`、`OnEvent`、`Log`、`GetResource`。实现 `ModContext` 也不持有 `GameEngine`。缺口是事件回调里的 `ScriptContext.GetEngine()` 把整颗引擎交出去，合同等于没写。

目标端口（仍是接口，实现留在 Engine）：

| 端口 | 替换今天的什么 |
|------|----------------|
| 已有 `OnEvent` / `VFS` / `Log` / 两个 Registry | 保持 |
| `ISystemRegistrar` | `engine.RegisterSystem` / `RegisterPresentationSystem` / `InsertSystemBeforeRequired` |
| `IRegistrySetView` | 静态 `TagRegistry` / `AttributeRegistry` / `GraphIdRegistry` / `TeamManager` 的登记与查询 |
| `IServiceView` | `engine.GetService` / `SetService` / `GlobalContext` 里已有的 `CoreServiceKeys` |
| `IConfigView` | `MergedConfig` / `ConfigPipeline` / `ConfigCatalog` |
| `ISpatialSession` | `SetCoordinateConverter` 与空间查询；转换器由地图/board 注入，Mod 不得改全局 |
| `ScriptContext.GetWorld()` | 已存在，继续用 |

`SystemFactoryRegistry.Register(name, group, factory)` 已经是正规扩展点，应成为「登记系统」的默认路径。直接 `RegisterSystem` 必须带能力声明（capability id + 目标 phase），没有声明的调用在分析器里失败。

#### 3.3.2 `GetEngine()` 怎么退役

定义在 `src/Core/Scripting/ScriptContextExtensions.cs`：`ctx.Get(CoreServiceKeys.Engine)`，返回具体 `GameEngine`。

退役顺序：

1. 标 `[Obsolete]`，消息指向上表端口。Core 内部（如 `LoadMapCommand`）暂时保留，或迁到 Hosting 内部扩展。
2. 扩展方法移出 Mod 能引用的契约程序集。Mod 编译不到 `GetEngine`。
3. 调用清零后删除方法与 `CoreServiceKeys.Engine` 对 Mod 的暴露。

`src/Libraries/Ludots.WebUI/IWebUIBridge.cs` 的文档注释示范了 `context.GetEngine()?.GlobalContext[...]`。退役时改写成从 `IModContext` / 宿主注入的 `IWebUIBridgeFactory` 取桥，与接口自己的正文一致。

#### 3.3.3 调用点怎么分批（按「拿引擎干什么」而不是按目录一刀切）

基线 `82ddb3322a` 上 `.GetEngine(`：

| 范围 | 次数 | 文件 |
|------|------|------|
| `mods/` | 203 | 145 |
| `src/Core/Commands/LoadMapCommand.cs` | 1 | 1 |
| `src/Libraries/Ludots.WebUI/IWebUIBridge.cs`（文档注释） | 1 | 1 |
| **调用合计** | **205** | **147** |
| 定义 `ScriptContextExtensions.GetEngine` | 1 | 1 |

67 个 Mod 家族用到。计划原文写「mods 里 205 处」——按「全仓 `.GetEngine(` 调用」数是 205，按「只计 mods」是 203。本设计以本基线重数为准。

同一批文件里，拿到引擎之后最常见的成员（近似，同一文件内所有 `engine.X`，不是每次 GetEngine 一对一）：`World`、`GetService`、`GlobalContext`、`CurrentMapSession`、`SetService`、`RegisterSystem`、`RegisterPresentationSystem`。

分批（每批合入后棘轮只降不升）：

| 批 | 迁什么 | 验证 |
|----|--------|------|
| M1 | `World` / `GetWorld` / 已有 `CoreServiceKeys` | 这些文件不再出现 `GetEngine` |
| M2 | `RegisterSystem` / `RegisterPresentationSystem`（mods 内 `RegisterSystem` **100** 次、65 文件、35 个 Mod；出现的组：`InputCollection` 66、`PostMovement` 28、`RuntimeEntityBinding` 9、`AbilityActivation` 3、`ClearPresentationFlags` 1、`EffectProcessing` 1） | 新调用必须走 `ISystemRegistrar` 或 `SystemFactoryRegistry`，且带能力声明 |
| M3 | `GetService` / `SetService` / `GlobalContext` | 只允许 `CoreServiceKeys` / Mod 自有 `*ServiceKeys` |
| M4 | 配置只读 | `IConfigView` |
| M5 | 空间 3 处 `SetCoordinateConverter`（NodeGallery / AbilityGraphSandbox / GraphOpsSpatial） | `ISpatialSession`；converter 由 board 注入 |
| M6 | 静态表直写：`TeamManager` 7 家族；`TagRegistry.Register` 14 家族 / 36 次；`AttributeRegistry.Register` 11 家族 / 39 次；`GraphIdRegistry.Clear` 5 处 | 改走 `IRegistrySetView`；Clear 在 Mod 里编译失败 |
| M7 | 剩下的宿主能力（`LoadMap`、`ModLoader`、`Physics2D`） | 明示宿主端口，或留在工具/Engine 内部 |
| M8 | 删除 `GetEngine` | 全仓零引用 |

`RegisterSystem` 没有白名单——100 次直接进 6 个组。M2 的能力声明就是白名单：未知 capability 或未声明 phase 失败关闭。

---

### 3.4 跨层组件所有权：从文档变成编译期墙

S10（#952）已在 [实体仿真分层 §5.1](../../gitbook/architecture/entity-simulation-layering.md) 写了 write owner。**本设计不改那张表。** S10 把 partial-world 隔离划出范围；S14 也不做。

S10 §5.1 原文口径（实现墙必须服从）：

| 组件 | write owner | 读方 | 允许的非 owner 动作 |
|------|-------------|------|---------------------|
| `PresentationStableId` | 模拟侧分配器与 spawn/lifecycle | 表现侧消费 | 表现 bootstrap 只能在缺失时补挂已分配 id |
| `PresentationDestroyPending` | 模拟侧 lifecycle | 表现侧消费并 finalize；L0 可读以便拒绝已 pending 的 txn | 表现侧可 Remove 以完成销毁 |
| `CullState` | `CameraCullingSystem` 写 `IsVisible` / 视觉 LOD | 仅表现侧 | 模拟 spawn 可挂默认隐藏值；模拟相不得读取 |

`VisualTransform` 不在 §5.1 表里，但 S10 §5 已经裁定：只从 `WorldPositionCm` 插值、不反写；`InputCollection` / `AbilityActivation` / `EffectProcessing` / `AttributeCalculation` 不得读它。本设计把这条当作已有 owner 裁决来砌墙，不回头改 S10 的表。写方是表现插值系统（今天包括 `WorldToVisualSyncSystem` 这条表现登记路径）。

类型放哪 = 墙：

| 组件 | 类型住在 | 谁能写出这个名字 |
|------|----------|------------------|
| `PresentationStableId`、`PresentationDestroyPending` | `Ludots.Contracts` | 模拟与表现都能看见；表现写权限靠分析器收 |
| `CullState`、`VisualTransform` | `Ludots.Presentation` | 只有 Presentation（和 Engine 宿主）能看见。Input / GAS / L2 / Spatial 引用即编译失败 |

同一份 Arch `World` 继续共用。墙是 **ProjectReference + 分析器**，不是第二份 World。

分析器（`Ludots.Analyzers.Layering`，ci-gate）：

- 标了 `[WriteOwner]` 的字段/组件，非 owner 程序集不得写。
- Presentation 对 `PresentationStableId` 只允许「缺失则补挂」的 bootstrap 符号表。
- 模拟相 system（按 `SystemGroup`：InputCollection、AbilityActivation、EffectProcessing、AttributeCalculation 等）不得出现 `CullState` / `VisualTransform` 类型。S10 已有部分文本/反射守卫；分析器接棒后删文本扫描。

今天四个组件都在 `src/Core/Presentation/Components/`。模拟 spawn 能写它们，只因为大家在同一个程序集。搬类型是 Wave 5 的事；Wave 4 先在原处打上 owner 属性并把分析器变红。

---

### 3.5 `SystemGroup` 顺序的单一数据源

#### 3.5.1 今天有几份

真正跑的是 `PhaseOrderedCooperativeSimulation.PhaseOrder`（11 项，含 `RuntimeEntityBinding`）。`enum SystemGroup` 声明顺序目前与它一致，但执行不读枚举顺序——漏一项就静默不跑。

第三份在测试里：`ArchitectureGuardTests.SystemGroup_MustMatchDesignDocument` 手写名字列表。基线仍用 `Is.EquivalentTo`（集合相等，不比顺序）。S8（#951）把这里改成 `Is.EqualTo`，并增加与 `PhaseOrder` 的交叉校验。那是临时守卫，不是根治。

第四份在文档：`gitbook/contributing/ai-assisted-development.md` 与 `gitbook/architecture/runtime-overview.md` 的 phase 列表都 **漏了 `RuntimeEntityBinding`**（10 项对 11 项）。多份副本已经在骗人。本设计不改 gitbook（合同不标已落地）；根治落地的那一票再回写正式页，且文档只能转述枚举，不能另造一张表。

#### 3.5.2 根治

1. `SystemGroup` 搬到 `Ludots.Contracts`。
2. `SystemGroupOrder.All` 的实现就是 `Enum.GetValues<SystemGroup>()`。枚举声明顺序 = 执行顺序。
3. `PhaseOrderedCooperativeSimulation` 迭代 `SystemGroupOrder.All`。删除 `PhaseOrder` 数组。
4. 测试：运行时序列 `== Enum.GetValues<SystemGroup>()`。删除手写 `DesignedSystemGroupOrder`。
5. 新增 phase = 在枚举里插入到正确位置。没有第二张表可忘。

S8 若已先合入：删掉它新增的那份交叉校验数组，改测「没有第二张表」。S8 若尚未合入：Wave 1 直接做根治，不必先落地临时守卫再拆掉。

---

### 3.6 迁移路径、每批验证、CI、防回退

不是「下周全切完」。每一波一个（或一组紧密）PR，绿了再做下一波。

| 波 | 做什么 | 不做什么 | 怎么证明没回退 |
|----|--------|----------|----------------|
| **0** | 本设计评审 | 搬文件、改 csproj | 本文 + UAT |
| **1** | `SystemGroupOrder`；`GetEngine` 标过时；调用次数棘轮；禁止 Mod 新增 `GraphIdRegistry.Clear` | 搬目录 | 见下方棘轮；`SystemGroup` 测试改为枚举即顺序 |
| **2** | `ModRegistrySet` 实例化；统一冻结；生产 `Clear()` 消失 | 拆程序集 | 同进程两个引擎实例 id 空间隔离的测试；冻结后再 Clear/Register 必须抛 |
| **3** | 扩 `IModContext`；按 M1–M7 迁 `GetEngine` / `RegisterSystem` | 拆程序集 | `GetEngine` 次数只降不升；新 `RegisterSystem` 必须带能力声明 |
| **4** | owner 属性 + 分层分析器 | 搬组件文件 | 分析器 ci-gate；S10 的模拟相禁读变成编译期 |
| **5** | **一次剥一层**：Spatial → Input → Presentation → Graph 契约+Runtime（先把 `GraphControlFlowDocument` 调出 L0）→ Authoring → Behavior → GAS → Engine 门面收敛 | 同一 PR 剥两层 | 每层增加一条 ProjectReference 图测试；该层禁边变编译错误 |
| **6** | 删除 `GetEngine`、静态登记表、文本扫描式 GraphRuntime 守卫；合并 S8 留下的双份 `ArchitectureGuardTests` | 新功能 | 全仓零 `GetEngine`；ArchitectureTests 不再靠 `ReadAllText` 守层 |

Wave 5 的顺序理由：Spatial / Input 先剥，暴露对 Presentation 类型的暗依赖（已经存在）；Presentation 一走，`CullState` / `VisualTransform` 的墙立刻变成编译错误；Graph 先契约后 Runtime，再 Authoring，避免 L0 目录再夹带 L1 文档；GAS 最后，因为它是 handler 的主要登记方。

过渡期用 `Ludots.Core` 门面做 type-forward，所以 Wave 5 不必同时改 167 个 csproj。每剥一层，把门面上对应的 forward 留下、实现挪走。Mods 改引用放到该层稳定之后，不和要求「第一个剥层 PR 改完所有 Mod」。

#### CI 怎么守

1. **引用图测试（编译后 API，禁止 `ReadAllText` 守层）。** 对每个实现程序集断言禁止的 `ProjectReference` / 程序集引用不存在。这替换 C21 那条目录文本扫描。
2. **`Ludots.Analyzers.Layering`。** owner 与「模拟相不得出现表现类型」。进 ci-gate。
3. **棘轮测试。** 把下列整数写进测试常量，CI 断言实际值 `<=` 常量；常量只许下降：
   - `.GetEngine(` 次数
   - mods 内直接 `RegisterSystem(` 且无能力声明的次数
   - 生产代码 `GraphIdRegistry.Clear(` / 静态 `Clear(` 次数
   - 引用 `Ludots.Engine`（或门面里的 `GameEngine`）的 Mod 项目数
4. **冻结合同测试。** 新实例 → 登记 → 冻结 → 再登记抛；冻结后不存在解冻 API。
5. **双引擎隔离。** 两个 `ModRegistrySet` 各自登记同名图，id 不得串。
6. **守卫 DRY。** `ArchitectureTests` 与 `GasTests` 不得再各持一份逐字节复制的 `ArchitectureGuardTests`。基线上两份各 72 次 `File.ReadAllText`，合计 144，正是 B24 的数。S8 删副本；S14 把剩下的层守卫换成引用图 + 分析器。

基线上 `src/Tests` **没有** 现成 Analyzer 项目。分析器是 Wave 4 新增，不是本设计伪称已有。

---

### 3.7 现状计数（必须用命令复算）

数字全部来自 `82ddb3322a`。换基线请重跑附录 A。数不清的标「约」并给出命令。

| 项 | 数 | 口径 |
|----|----|------|
| `.GetEngine(` 调用 | 205 | mods 203 + src 2；另有定义 1 |
| 使用 GetEngine 的 Mod 家族 | 67 | `mods/` 下按 showcase/capability/根目录分桶 |
| mods 内 `.RegisterSystem(` | 100 | 65 文件 / 35 家族 / 6 个 `SystemGroup` |
| 引用 `Ludots.Core.csproj` 的项目 | 167 | 全仓 `*.csproj` |
| 静态 `*Registry` | 19 | `src/Core` 内 `public static class \w+Registry` |
| `GraphIdRegistry.Clear(` | 5 + 1 + 58 + 1 | mods / Core loader / tests / tools |
| `TeamManager.` | 7 家族 / 8 文件 / 17 次 | mods |
| `TagRegistry.Register(` | 14 家族 / 36 次 | mods；审计旧文写「4」是当时快照，本基线不是 |
| `AttributeRegistry.Register(` | 11 家族 / 39 次 | 同上 |
| `SetCoordinateConverter`（mods） | 3 文件 | 与审计一致 |
| 两份 `ArchitectureGuardTests` 的 `ReadAllText` | 72 + 72 = 144 | 与 B24 一致 |
| ArchitectureTests 全目录 `ReadAllText` | 108 | 13 个文件 |
| `SystemGroup` 枚举项 | 11 | 含 `RuntimeEntityBinding` |
| gitbook phase 列表 | 10 | 漏 `RuntimeEntityBinding` |
| Core 顶层目录 | 约 40 | `ls src/Core`；本阶段只剥其中 Graph/GAS/Presentation/Input/Spatial |

审查原文 B22 的「TagRegistry / AttributeRegistry(4)」在本基线对不上 `Register` 家族数。以本表为准，不要把 4 抄进实现票。

---

## 4 场景

维护者打开解决方案时，应当能从项目引用看出：图的执行引擎看不见技能结算实现，技能结算看不见镜头裁剪，输入看不见画面上的实体。作者写 Mod 时，手里只有上下文对象能登记「我要在移动之后跑一个系统」「我要登记一个标签」，拿不到整台引擎，也清不掉别人的图表。

若有人把裁剪结果拿去决定「这个单位能不能点」，编译器或分析器在提交前挡住，而不是上线后靠文本扫描追认。若有人在枚举里加了一个新的帧阶段却忘了改第二张表——不会发生，因为没有第二张表。

同进程开两张地图、或测试连续装两个 Mod，图表编号不会因为有人调用了清空而和另一份程序表对不上。冻结之后不能靠清空假装没冻结过。

---

## 5 边界

**本设计包含**

- 程序集切分、契约/实现边界、依赖方向
- 注册表实例化与冻结合同
- Mod 可见面与 `GetEngine` 分批退役
- 把 S10 owner 砌成类型位置 + 分析器墙
- `SystemGroup` 单一数据源
- 分波迁移、棘轮、CI

**本设计不包含、也不授权**

- 本票搬文件、改 csproj、改生产代码
- 一次性把 Core 拆完
- 把 gitbook 合同改成已落地
- 新增 AAC 平行 ADR
- 删除八个 GraphOps 家族 Mod
- partial-world / 第二份 Arch World
- 重写 S10 的 owner 表
- 平行虚拟机、第二套 opcode、第二套系统组
- 本阶段拆 Navigation / Vision / Persistence / Association
- 放宽「禁止 fallback / 禁止静默失败」

**与邻票的关系**

- S6 停掉生产 `GraphIdRegistry.Clear()`：与 Wave 1/2 同向，不替代实例化。
- S8 假防线：先删双份守卫、把顺序比较补上；S14 再抽掉第二张表。
- S9 / S12：L2 走正式执行前门之后，`Ludots.Graph.Behavior` 才剥得干净。
- S10：owner 文档已在；本设计只说墙怎么砌。

---

## 6 UAT

```gherkin
Feature: 层与层之间的墙在编译期就在

  Scenario: 设计能回答边界问题
    Given 一份分层物理化设计
    When 维护者评审
    Then 它必须回答六个问题：切几个程序集、注册表如何实例化、Mod 能看见什么、跨层组件墙怎么砌、帧阶段只有一份顺序、迁移如何分批
    And 每一批都能单独验证、并能查出回退

  Scenario: 图的执行引擎编译不到技能结算和画面
    Given 维护者打开图执行那一层的项目
    When 有人试图直接使用技能效果类型或镜头裁剪类型
    Then 项目引用不允许这条边
    And 文本扫描不再是唯一的守卫

  Scenario: 作者拿不到整台引擎
    Given 作者在写一个 Mod
    When 他想登记系统、读配置、查空间、登记标签
    Then 他只能通过 Mod 上下文上的端口完成
    And 他写不出「把引擎给我」这种调用
    And 他清不掉进程里别人的图表

  Scenario: 换一张地图不会洗掉另一张地图的表
    Given 同进程里已经有一份冻结的登记表
    When 测试或第二份引擎需要空表
    Then 他们新建一份实例
    And 不存在把冻结标志拨回去的清空方法

  Scenario: 镜头看不见的单位仍然可以被选中和下令
    Given S10 已经规定谁拥有裁剪状态和视觉位姿
    When 维护者把这些类型放进表现程序集
    Then 输入与技能结算编译不到这些类型
    And 模拟侧的稳定身份与待销毁标记仍按 S10 由模拟写入、由表现读取
    And 没有人发明第二份世界来隔离组件

  Scenario: 帧阶段只有一份名单
    Given 维护者要加一个新的固定步阶段
    When 他只改枚举的声明顺序
    Then 运行时按这个顺序执行
    And 测试里没有第二份手写名单可以跟枚举漂掉
    And 漏写第二张表导致整组系统不跑的事不再可能发生

  Scenario: 迁移中途不能把旧门又开开
    Given 某一波已经把「拿引擎」的次数压下去
    When 后来的改动想再增加一次
    Then CI 失败
    And 次数上限只允许往下调
```

---

## 附录 A 计数命令

在仓库根、基线 `82ddb3322a`（或实现票自己的 HEAD）重跑：

```bash
# GetEngine 调用（不含定义）
python3 - <<'PY'
import re
from pathlib import Path
rx = re.compile(r"\.GetEngine\s*\(")
for root in ["mods", "src"]:
    total = files = 0
    for f in Path(root).rglob("*.cs"):
        n = len(rx.findall(f.read_text(encoding="utf-8", errors="replace")))
        if n:
            files += 1; total += n
    print(root, "calls", total, "files", files)
PY

# mods 内 RegisterSystem
rg -c --glob '*.cs' '\.RegisterSystem\s*\(' mods | awk -F: '{s+=$2; n++} END{print "calls",s,"files",n}'

# 静态 Registry
rg -n --glob '*.cs' 'public static class \w+Registry' src/Core

# GraphIdRegistry.Clear
rg -n --glob '*.cs' 'GraphIdRegistry\.Clear\s*\('

# TeamManager / TagRegistry.Register / AttributeRegistry.Register / SetCoordinateConverter
rg -n --glob '*.cs' 'TeamManager\.' mods
rg -n --glob '*.cs' 'TagRegistry\.Register\s*\(' mods
rg -n --glob '*.cs' 'AttributeRegistry\.Register\s*\(' mods
rg -n --glob '*.cs' 'SetCoordinateConverter\s*\(' mods

# 两份 ArchitectureGuardTests 的 ReadAllText
rg -c 'File\.ReadAllText' src/Tests/ArchitectureTests/Governance/ArchitectureGuardTests.cs \
  src/Tests/GasTests/Integration/ArchitectureGuardTests.cs

# 引用 Core 的 csproj
rg -l 'Ludots\.Core\.csproj' --glob '*.csproj' | wc -l

# SystemGroup 枚举 vs 文档列表
rg -n 'enum SystemGroup' -A 40 src/Core/Engine/GameEngine.cs
rg -n 'SchemaUpdate' gitbook/architecture/runtime-overview.md gitbook/contributing/ai-assisted-development.md
```
