# 图能力唯一入口

**图能力相关的进度、还开着的活、不该合的 PR，只认本页。**  
不要另写交接，不要从旧审计开工，不要再开一张「总入口」票。

规矩在这两页，不在本页改：[图怎么分层](graph-layering-flow-and-behavior.md)、[纯计算和可挂起动作怎么分开](graph-funclib-actionlib-contract.md)。  
展厅列表看 [能力标准展厅](capability-standard-showcases.md)。打分短剧怎么验收看 [残血的分更高](../acceptance/graph-score-wounded-priority.md)。  
旧审计在 `docs/audits/`，那是当时的本子。和本页打架，听本页的。

---

## 1. 概述

进游戏能玩的，这轮收好了。工程分层没有拆完。总规矩仍写「修复中」，别改成做完了。

打开启动器：每个图节点自己一间短剧，大约一百二十间。图能力这条线没有按家族打包的大杂烩，也不再留八家族旧房间的退役卡。「残血的分更高」能进，字幕点名残血，残血掉血、满血不动。

这里说的退役卡只指图能力八家族。仓库画廊可以保留别的历史追溯卡，例如旧 Physics2D 游乐场；那不是本轮图能力入口。

三张票已经进主干，票面还开着，收口只剩关单；真正还在做的，是一件不能假装做完的分层，再加上两张先别关的总账。别的不要当这轮图能力去合。

---

## 2. 结构

```text
唯一入口 = 本页

已经做好    →  别重做
已合主干    →  护盾演示 / 内存门槛 / 干净构建；只剩关单
先别关的总账 →  每个节点都要能写能看；作者只走一条边
分层        →  架子有了，墙没有，另开活，别和上面捆
不要碰的    →  打分预算、面板、助手、过期审计草稿
```

---

## 3. 详情

### 3.1 已经做好，别重做

- 每个还能运行的图节点，都有一间能看懂的短剧。
- 八间按家族打包的大杂烩已经删掉。图能力这条线不再留这些旧房间的退役卡片。
- 「残血的分更高」能玩。字幕只读这一刀选中的人和分，不再另算一遍。
- 加减乘钳那几间，说的是示意条，不是结算出来的伤。
- 一张图自己调自己，登记时就拒绝，游戏还在。
- 写进生命的数，不会过两拍自己变回去。
- 查询图只填一张会等一拍的动作、不填函数名，编译失败。
- 容量到顶必须报错。
- 点人、下令看战场上的位置，不看镜头挡没挡住。
- 表上写「测过了」，就必须真跑到那个节点。
- 巡逻树、门岗可以写在配置里。哨兵机挂「等一拍」，加载就失败。
- 图程序都要显式结束。查询、结算、打分、校验、派生和脚本，都走同一条 `HaltReturnInt` 终点。
- 动作库登记必须写明宿主；行为树、门岗、关卡脚本不靠默认宿主蒙混过去。

看见旧本子还在说「门没锁 / 打分没合 / 房间只是退役」，那是过期句子。

### 3.2 已合主干，待关单（别再当实现票）

**第一件：Moba 护盾演示。**
主干已经收进去了。现在这张票只剩关单，不要再当成新的实现目标。
→ https://github.com/MightyBubble/Ludots/issues/916

**第二件：有一条「不许乱占内存」的门槛。**
主干已经收进去了。别为了关单把门槛放宽。
→ https://github.com/MightyBubble/Ludots/issues/917

**第三件：干净构建时同名文件打架。**
主干已经收进去了。这里是卫生债，不是玩法。
→ https://github.com/MightyBubble/Ludots/issues/918

**第四件：Query 图契约（#1084）。**
Query 纯读、显式 subject、缺 subject 失败关闭、精确输出、无 Store/事件/动作/continuation 的合同已由主干 GraphReturnWriter/操作策略与回归测试覆盖，本页只记关单。
→ https://github.com/MightyBubble/Ludots/issues/1084

**第五件：TriggerGraph/Dialogue 统一 QueryGraphGateway（#1099）。**
显式 subject + pins、目标必须已登记 GraphKind.Query、typed Bool/Int/Float/Entity/EntitySet、缺失/类型不符失败关闭、禁止 Query 动作/事件/Store/continuation、不新增第二 VM 的统一 Query 网关合同已由主干 GraphReturnWriter/操作策略/编译器与回归测试覆盖（TriggerGraph 程序走同一 GraphExecutor，不经 Query 网关），本页只记关单。
→ https://github.com/MightyBubble/Ludots/issues/1099

这五张票都已经进主干；本页只记关单，不再派实现票。

### 3.3 真正还在做的

**编辑器里程碑（控制流与 live debug 已收口，文本能力未收口）。**
节点联想只从运行时 descriptor 获取；Bridge 同时投影 `BranchBool`、`SwitchInt`、`Wait`、`While`、`Until`、`Break` 六种作者糖及其控制/值端口。`Jump.target`、`Call.call/next` 等普通控制端口也来自 Bridge descriptor，React 不维护第二份 op 端口表。`Break` 编译时严格降低为带显式 `target` 边的 Jump；`Select` 仍明确是实体选择 `SelectEntity`，不是尚不存在的通用 Select。编辑器连线、删节点后的悬挂边清理、布局数据校验和 live trace source map 校验均走失败关闭。

Live debug 记录实际执行节点归因、Yield/预算挂起、Halt、游标、引脚和黑板变化；嵌套 `InvokeScript` 继承固定容量 trace 并携带子图 id。当前不伪造 `NodeExit` 生命周期事件。黑板 buffer 缺失仍在运行时明确失败；实体能力在 authoring 阶段的声明和编译校验仍是下一条合同切片，不能把运行时隐式安装路径写成已完成。

字符串花括号自动引脚、字符串寄存器、组合文本与 `Concat` 仍未完成。当前运行时没有正式的 text value、固定容量/零分配传递、符号 patch 和 presentation sink 合同，因此编辑器不会展示可保存但运行时不可执行的假节点。它们必须作为独立基建切片先补齐合同，再进入 descriptor 名册。

作者面还开着的债，不要当成新发现再审：执行线没下一步就该结束，但先改“必须显式停下”的合同 https://github.com/MightyBubble/Ludots/issues/1107 。蓝图变量面板 MapVariable 作者面已随 Narrative PR #1222 / Bridge 进主干，#1109 只剩关单。#1108 要对齐的是「地图上具体 InstanceId（单位/区域）当变量拖取」——单实体 `LoadPlacedEntity` + Placed 栏已落地；区域/锚点只读变量若还缺再补，不是数组/映射集合类型。事件入口露出本次载荷（#1106）、放置实体读、地图变量变更事件（#1113）、图互调/跨图派发/全局订阅与 hook（#1115/#1116/#1123/#1124）、纯数据枚举（#1125）、图↔代码 AwaitCallback 续跑（#1126）已随 night-raid 大包进主干（PR #1239）；合入后把对应票改成关单卫生，不要再派实现票。#1126 落地范围：`AwaitCallback=455` + `GraphCallbackService` + `SystemGroup.Continuation` 按注册序 Drain；TriggerGraph 挂载可直接挂起；嵌套 `InvokeScript`/`InvokeGraph` 仍禁 Yield/AwaitCallback（同步函数）。可等待复用走编译期糖 `InlineGraph`（`TriggerGraphInlineWeaver`，虚幻 Macro 风格，Await 落在宿主程序）。Dialogue 宿主 Completer 已接线：玩家确认选项/推进台词时 `TryCompleteByCallbackType(DialogConfirm)`，不另造第二套等待。未完成前，编辑器不得画出保存后引擎不认的假针脚或假集合。

**分层：架子有了，墙没有。**  
工程里多了两份薄的契约，核心工程还是一大坨。展厅大多还能一把抓住整台引擎。把空间、输入、画面、结算真正拆开，以及不许再抓整台引擎，这两步没做。要做就单独开活，对照 `docs/audits/s14_layering_physicalization_design.md`，别和修演示、修构建捆在一起。没拆完之前，总规矩继续写「修复中」。

**两张总账先别关。**  
每个图节点都要能写、能测、能看见：https://github.com/MightyBubble/Ludots/issues/915  
作者只走一条边、只进一扇门：https://github.com/MightyBubble/Ludots/issues/861  
画廊和一批门已经合了。三张旁路票只剩关单，分层没拆完，所以总账还开着。

新开了一条线，别当成图能力收口的回锅：触发器图（TriggerGraph，原 MapTriggerGraph）。
进度与计划只认两张票：地图域线 https://github.com/MightyBubble/Ludots/issues/1030 ；域扩展线（实体域挂载、GAS 事件桥、技能/效果时刻桥、presenter 时序合同）https://github.com/MightyBubble/Ludots/issues/1031 ——两张票顶部各有进度快照与剩余切片清单，新活从快照开工，别重做已落地的。
方言/挂载、事件词典（MapHeartbeat 地图心跳/实体死生/区域）、地图变量存储、时间线续跑、实体域挂载、GAS 桥、「夜袭三波」全数据旗舰与旧 LevelDirector 试验线退役，都已落地；2026-08-24 又补上技能域 `abilities.json.triggerGraphs`、Mod 域 `mod.json.triggerGraphs` 和显式 `route: global` 跨地图路由，统一复用现有 TriggerManager/TriggerGraph VM。2026-08-26 night-raid 大包（rebase 最新 main，PR #1239）继续把事件 Schema SSOT、全局订阅表/`FireGlobalEvent`/`FireCrossMapEvent`、图互调与放置实体读、Enum 目录、图编辑器作者面 hardening 收进同一条线；真正的跨图派发走 `FireGlobalEvent`，不再靠 FireMapEvent 扇出旧表。剩余收口是 S4 时序合同全文对齐与 S5 实体/技能真实可玩 showcase、画廊和 AgentBridge 运行证据，不能把 headless 基建测试写成 showcase 完成。图侧 spawn 动词已经落地：SpawnTemplate（GraphNodeOp 447）在 TriggerGraph 与 Script 都能用，「夜袭三波」旗舰的 stage3 就用它在图内生成 boss（`mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graphs.json` 的 `spawn_boss` 节点）。合不合、什么时候合，看 #1031 的最新进度快照。

又开了一条线：行为树「真图化」（BT-1）。设计冻结本在 artifacts/showcases/graph-fsm-bt-refactor-design.md。已落地：`BtSequence` / `BtSelector` / `BtDecorator` 三个 Script-only 作者糖把整棵树内联成单个 Script 程序（`Call`/`Return` + `CompareEqInt` + `JumpIfFalse`，零新 opcode，状态寄存器 0/1/2）；`GraphBehaviorTreeHost` 做 per-agent 帧与 think wave 驱动，Yield 叶跨波恢复，嵌套深度对齐 `MaxCallStackDepth`。真实性判据锁在 `GraphBehaviorTreeSugarTests` / `GraphBehaviorTreeHostTests`（编译产物断言、糖 vs 手写图消融对照、解释器无关、Yield 跨波恢复、深度/预算失败关闭）。旧 `BehaviorTreeWorld`（C# JSON 树解释器）保留为旧数据路径，图路径不碰它的遍历。BT-B 已落地：arena 主树 `bt.patrolChaseAttack` 重写为糖图并由 `GraphBehaviorTreeHost` 逐波执行（旧 C# 树解释器对 arena 为破坏性移除）；条件/行动叶去空心化——判定在图（黑板读 + `CompareLtInt` 阈值 + Bool 终端尾声），C# 传感器降级为"胶水喂数"；10k crowd 段实测真图 9.5-15.8ms/波超预算，保留无图压测拓扑并在注册表 summary 显式标注（不再顶 graph 语义）。配套基建修复：寄存器分配器两遍化（pin 先保留，声明序不再决定 pin 合法性）、`HfsmState.Name` + `GetLeafStateName`（渲染按状态名取色，重排 hfsm.json 不再错色，有防重排测试）、`ValidateHierarchy` 父链环 fail-closed 守卫。**FSM-1a 已收口（验收已关）**：`FsmState` 糖 / `GraphFsmHost` / featured sentry arena 迁移落地后，`GraphFsmSugarTests.SentryGraphs_DeHollowed_AndFsmHostDrivesPhaseCycle` 锁死 `Graph.FSM.Sentry` 经 `GraphFsmHost` 的相位环真实性（近距离 Idle→Alert→Combat 保持、远距离 Retreat→Idle）；showcase.registry.json 回链的 CI 门 `HfsmSentryArenaShowcaseAcceptanceTests.RegistryName_DelegatesToSeparatedSuite` 现在额外断言生产 runtime（`HfsmSentryArenaRuntime`，intruder 距离驱动，非合成 feed）的 featured sentry 相位真的 idle→alert→combat 演进，不再只测 think 预算——CI 挂了就是 FSM-1a 回归。还开着的：Parallel（一期显式不支持）、子树复用/异步叶（BT-2）、HFSM 侧其余真图化（FSM-1b：enum 驱动 HFSM 状态机化，enum-driven-fsm showcase 仍未开工）。分层合同条款同步修订在 [图怎么分层](graph-layering-flow-and-behavior.md)。

又开了一条线：纯数据自定义枚举目录（#1125）。已落地：`Enums/enums.json` 走 ConfigPipeline（ArrayById + `ArrayAppendFields:["members"]`，mod 侧 config_catalog.json 声明）装载成 `EnumCatalog`；成员值=首次声明的顺序索引，后 mod 只能追加成员、同名重声明 fail closed 点名，未知字段/缺 id/非法成员名/缺 members 全 fail closed。`SwitchInt` 节点可绑 `enumType`，case 边写成 `case:成员名`，编译期查目录解析成 int 再走原 SwitchInt 路径（消融测试锁死：与手写 `case:1` 指令序列逐条一致），指令 source map 保留 `case:Combat` 原始拼写；enumType 未注册、成员名不在枚举内、绑定时写裸 int 全 fail closed。新作者糖 `SelectByEnum`（selector + case:成员名 候选 + 可选 default）展开 ConstInt+CompareEqInt+JumpIfFalse+MoveInt 链，零新 opcode/执行器。事件参数可注解 `enumType`（int 参数专属，EventParamType 不加 Enum 成员，防回归断言在 `EnumCatalogTests`）。GameEngine 装载序：枚举目录先于事件目录；编译通道 `Compile(doc, eventSchemas, enums)` 可空参数；Bridge validate 同源聚合，`/api/graph/enums/{modId}` 供编辑器下拉。showcase 一期不做：enum-driven-fsm 归 FSM-1 载体（artifacts/showcases/enum-driven-fsm-showcase-design.md 明说依赖 #1113+本票）。→ https://github.com/MightyBubble/Ludots/issues/1125

下面这些早就知道、还没做，**不要当成新发现再审一轮**：默认「看见敌人 / 进入射程」还要有人先塞数字；图号在代码里还是普通整数；有一条事件丢弃计数永远是零；两个节点钉同一格时说不清。

### 3.4 不要当成这轮图能力去合

- 过期审计草稿，已经被后来的本子取代。卫生上该关，不是功能缺口。https://github.com/MightyBubble/Ludots/pull/961
- 打分预算是另一件事。打分短剧已经能玩，别和预算捆。https://github.com/MightyBubble/Ludots/pull/723
- 面板是另一条线。https://github.com/MightyBubble/Ludots/issues/886
- 助手工具无关。https://github.com/MightyBubble/Ludots/pull/947

把本页和这次图基建收口写进仓库，走 https://github.com/MightyBubble/Ludots/pull/969 。这不是单纯文档改动；它同时收紧了登记、显式结束、动作宿主和压力门。合进去之后，入口就是本页，不再是那张 PR。

### 3.5 两份自审别盖掉

脚本方言拓宽时的自审正本：`artifacts/gas-composition-gate.md`。后开的活不许覆盖它。  
这次图基建收口自己的自审：`artifacts/gas-composition-gate-pr969-graph-closeout.md`。
打分短剧自己的自审：`artifacts/gas-composition-gate-graph-score-showcase.md`。

---

## 4. 场景

1. 新人接手。打开本页。知道能玩什么、还修哪几件、别碰什么。不再去翻十几份旧审计拼现状。
2. 我打开启动器。大约一百二十间短剧。没有大杂烩，也没有八家族旧房间的退役卡。
3. 我走进「残血的分更高」。不用点技能。字幕点名残血木桩，写出这一刀的分。残血掉血，满血不动。
4. 我打开 Moba 演示。它已经能起来。那三张票只剩关单，不再是新实现目标。
5. 我打开工程。能看见两份薄契约。核心工程还是一大坨。总规矩第一行仍是修复中。

---

## 5. 边界

- 本页是唯一入口。不准再写第二份「图能力交接」。
- 本页只说进度和还开着的活。改规矩去那两份合同页。
- 不准把「多了两份薄工程」写成「分层做完了」。
- 不准把已删的大杂烩写成「图能力这条线还有退役卡」。
- 不准把别的领域的 retired 追溯卡反过来说成图能力没删干净。
- 不准把「残血的分更高」写成还没合。
- 旧审计过期了就改正文，不准只加一句「去别处看」。
- 有具体房间坏了、具体演示启动不了，再开活。不要为了再审一遍而再审一遍。

---

## 6. UAT

```gherkin
Feature: 接手的人只看一页

  Scenario: 我知道从哪进
    Given 我是新来的
    When 有人问图能力现在怎样、还开着什么
    Then 只指这一页
    And 没有第二份交接

  Scenario: 我知道哪些只剩关单
    Given 我读完本页
    When 我要动手
    Then 我先把那三张票当成关单卫生
    And 我继续分层和两张总账
    And 我不会去重做已经能玩的短剧

  Scenario: 启动器里没有大杂烩
    Given 我打开启动器
    When 我翻展厅
    Then 我看到每个图节点自己一间短剧
    And 我看不到按家族打包的房间
    And 我看不到这八间图能力家族的退役卡片

  Scenario: 残血的分更高能看懂
    Given 我走进「残血的分更高」
    When 我站着看完第一刀
    Then 字幕点名残血木桩
    And 字幕写出这一刀的分
    And 残血掉血，满血不动

  Scenario: 没有人告诉我分层已经做完
    Given 我打开工程
    When 我去看「纯计算和可挂起动作怎么分开」的第一行
    Then 上面写的是修复中
```

---

## 附录：对 GitHub 时再看

正文用不到这些编号。

已经合进主干：941、944、945、946、948、950、951、952、953、954、956、957、959、960、962、963、964、965、966、967、968。

已合进主干、只剩关单：916、917、918、1084、1099。

还开着、本页点过名的：915、861。

不要当这轮去合：961、723、886、893、947。

本页和图基建收口：969。
