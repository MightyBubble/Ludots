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
主干已经收进去了。票面若仍开着，只是关单卫生，不要再当成新的实现目标。
→ https://github.com/MightyBubble/Ludots/issues/916

**第二件：有一条「不许乱占内存」的门槛。**
主干已经收进去了。别为了关单把门槛放宽；票面该关。
→ https://github.com/MightyBubble/Ludots/issues/917

**第三件：干净构建时同名文件打架。**
主干已经收进去了。这里是卫生债，不是玩法；票面该关。
→ https://github.com/MightyBubble/Ludots/issues/918

**第四件：Query 图契约（#1084）。**
合同与回归已在主干；GitHub **已关**。本页不再派实现。
→ https://github.com/MightyBubble/Ludots/issues/1084

**第五件：TriggerGraph/Dialogue 统一 QueryGraphGateway（#1099）。**
合同与回归已在主干；GitHub **已关**。本页不再派实现。
→ https://github.com/MightyBubble/Ludots/issues/1099

这五张：实现都在主干。#916–#918 若仍 open 就关单；#1084/#1099 已关。

### 3.3 真正还在做的

**编辑器里程碑（控制流与 live debug 已收口；正式文字合同已齐）。**
节点联想只从运行时 descriptor 获取；Bridge 投影作者糖及其控制/值端口（含 `BranchBool`、`SwitchInt`、`SelectByEnum`、`FsmState`、`Wait`、`While`、`Until`、`Break`；Script 另有 `BtSequence` / `BtSelector` / `BtDecorator` 与动态 `child:{n}`；TriggerGraph 另有 `InlineGraph`）。`Jump.target`、`Call.call/next` 等普通控制端口也来自 Bridge descriptor，React 不维护第二份 op 端口表。`Break` 编译时严格降低为带显式 `target` 边的 Jump；`Select` 仍明确是实体选择 `SelectEntity`，不是尚不存在的通用 Select。编辑器连线、删节点后的悬挂边清理、布局数据校验和 live trace source map 校验均走失败关闭。地图变量面板只暴露 Integer / Float，不再列出引擎还不认的 Array / Map。

Live debug 记录实际执行节点归因、Yield/预算挂起、Halt、游标、引脚和黑板变化；嵌套 `InvokeScript` 继承固定容量 trace 并携带子图 id。编辑器侧按 Flow Canvas 方式点亮节点/控制边并贴 pin 芯片，`drain` 事件带 `controlPort`；当前不伪造 `NodeExit` 生命周期事件。黑板 buffer 缺失仍在运行时明确失败；实体能力在 authoring 阶段的声明和编译校验仍是下一条合同切片，不能把运行时隐式安装路径写成已完成。

底栏用人话讲这一趟，数据是 mod 自己的：`mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graph_editor.json` 的 `annotations`（节点分组每图声明一次 + 按入口写抬头），Bridge 读写都对着 `graphs.json` 核对分组节点与入口标签，改名失败关闭并点名。底栏按执行到达顺序列出走过的每一组，和画布热度同一个 TTL 一起冷掉。编辑器源码里不得出现具体图 / mod / 节点 id，`ReactEditor_MustNotNameShowcaseGraphsOrMods` 扫全前端目录守这条。入口起因是「等事件」或「等输入动作」的单选，运行时 `event` / `action` 恰有其一；动作 id 从 `/api/graph/input-actions/{modId}` 合并目录下拉，保存路径跑 `RequireTriggerGraphEntryShape`。编辑器前端已进 CI（`graph-editor-frontend`：tsc + 图编辑器目录 lint + 断言脚本）。

trace 记录只有序号和步数，没有时间或帧号：一拍跑完的链是齐亮齐灭，不是逐步流动。要真做流动，先给记录补时间或帧号，别在文档里先许诺。

字符串花括号自动引脚、字符串寄存器、组合文本与 `Concat` 的运行时合同已落地：`GraphValueType.Text` + 固定容量 `GraphTextHeap`、`ConstText` / `ConcatText` / `IntToText` / `FloatToText` / `SinkPresentationText`，以及作者糖 `FormatText`（花括号自动引脚，编译期降为原子文字 op）。合同正本见 [图正式文字](graph-formal-text.md)；作者接法见 [拼句指南](graph-formal-text-authoring-guide.md)。玩家短剧「拼一句上字幕」见 [验收](../acceptance/graph-formal-text-subtitle.md)（`capability_standard_graph_formal_text`）。编辑器只从运行时 descriptor / 已登记糖露出可保存节点，不再留假 Concat。

TextKey 发现糖（Tag 式选键 → 真 i18n catalog）与 FormalText 字面量轨分离：可保存 op `LoadTextKey`、Bridge `/api/graph/text-keys/{modId}`、编辑器 `textKey` 选择器。合同正本见 [图 TextKey 发现糖](graph-textkey.md)。本切片零参；带参 `FormatTextKey`、ActiveLocale 对齐、生产 Dialogue drain sink 另线。

**图 Codegen 产品化（CG-0…CG-6 + 运行时装载已落地）。**  
正式程序集 `Ludots.Graph.Codegen`：F0–F3 特化发射（允许回边），其余家族 HandlerForward；coverage 全量 `covered`；Bridge 预览/对拍/覆盖；编辑器 Codegen 面板。运行时：`game.json` 键 `graphExecutionBackend`（`interpret` / `codegen` / `codegen-prefer`）在装图后绑定生成入口；`GraphExecutor` 优先走生成码；`ludots.graph.debug` 与 Live Debug 标题报 `executionBackend`。夜袭旗舰 `graphExecutionBackend=codegen`。合同正本 [图 Codegen 产品化](graph-codegen-productization.md)；自审 `artifacts/gas-composition-gate-graph-codegen-impl.md`。未知 op / 绑定失败在 `codegen` 模式失败关闭。

作者面债与票态以 §3.3.1 表为准，勿按过期长文派活。MapVariable 作者面、放置实体读、事件入口、跨图派发、枚举、AwaitCallback、`StartDialogue` 等已随主干落地；GitHub 上对应实现票多已关。编辑器不得画出保存后引擎不认的假针脚或假集合。

**分层：架子有了，墙没有。**  
工程里多了两份薄的契约，核心工程还是一大坨。展厅大多还能一把抓住整台引擎。把空间、输入、画面、结算真正拆开，以及不许再抓整台引擎，这两步没做。要做就单独开活，对照 `docs/audits/s14_layering_physicalization_design.md`，别和修演示、修构建捆在一起。没拆完之前，总规矩继续写「修复中」。

**两张总账先别关。**  
每个图节点都要能写、能测、能看见：https://github.com/MightyBubble/Ludots/issues/915  
作者只走一条边、只进一扇门：https://github.com/MightyBubble/Ludots/issues/861  
画廊和一批门已经合了。三张旁路票只剩关单，分层没拆完，所以总账还开着。

新开了一条线，别当成图能力收口的回锅：触发器图（TriggerGraph，原 MapTriggerGraph）。
GitHub 上 **#1030 / #1031 已关单**（2026-08-25）。地图域、实体/技能/Mod 挂载、夜袭旗舰、AwaitCallback 等已随主干落地；不要再把这两张票当活入口派实现。
若还有「技能专用可玩入口 / AgentBridge 真机证据」一类尾巴，**另开小票**，别重开整张 Epic 假装没合过。方言/挂载、事件词典、地图变量、跨图 `FireGlobalEvent`、SpawnTemplate（含夜袭 `spawn_boss`）都以当前 `main` 与 [分层合同](graph-layering-flow-and-behavior.md) 为准。

### 3.3.0 行为树 / HFSM：作者正轨是 L2（SSOT）

分层合同写死了：**L2 = 粗拓扑**（`behavior_trees.json` / `hfsm.json` + `BehaviorTreeWorld` / `HfsmWorld`），叶子再调 L1。
进度只认本页；分层规矩只认 [图怎么分层](graph-layering-flow-and-behavior.md)。

| 层 | 正轨资产 | 宿主 / 编辑器方向 |
|----|----------|-------------------|
| L2 BT | `AI/behavior_trees.json` | `BehaviorTreeWorld`；拓扑编辑器（见 PR #1416） |
| L2 FSM | `AI/hfsm.json` | `HfsmWorld`；拓扑编辑器（见 PR #1416） |
| L1 叶子 | ActionLib + Script 等 | `/gas-graphs` |

**2026-08 跑偏（仍在 main，不得当正统）：** agent「真图化」把旗舰整树/整机摊成 Script 糖（`BtSequence` / `BtSelector` / `BtDecorator` / `FsmState`）+ `GraphBehaviorTreeHost` / `GraphFsmHost`，还曾把 L2 JSON 路径降成「旧路径」。代码、测试、演武场 featured 糖图都还在——那是历史事实，不是产品收口。跑偏记录：[artifacts/showcases/graph-fsm-bt-refactor-design.md](../../artifacts/showcases/graph-fsm-bt-refactor-design.md)（**已不作 SSOT**）。

**扳回：** [PR #1416](https://github.com/MightyBubble/Ludots/pull/1416)（恢复 L2 为作者 SSOT）。合之前先 rebase；合入前不得再写「BT-1 / FSM-1a 图糖已收口」。
**另线：** BT Parallel、子树复用 / 异步叶（BT-2）——别和扳回捆一票。
Script 编辑器里仍可能露出 `Bt*` / `FsmState`（流程组合糖）；那只服务 Script 图，不表示「角色 AI 就该整树写成一张糖图」。

### 3.3.1 图相关还开着的（勿当新发现重审）

| 项 | 状态 | 该怎样 |
|----|------|--------|
| `#861` 作者只走一条边 | 总账开着 | 继续开着；别当实现票重做 |
| `#915` 每节点可写可测可看 | 总账开着 | 同上 |
| `#916` `#917` `#918` | GitHub 仍开；修复已在 main | **该关单**（卫生） |
| `#1108` 放置实体当变量 | 仍开；LoadPlaced* 等已落地 | **该关**或缩成残留小票 |
| `#1107` | GitHub **已关**；旧文曾写开着 | 保持关；勿再派 |
| `#1030` `#1031` | GitHub **已关** | 保持关；尾巴另开小票 |
| `#1084` `#1099` | GitHub **已关** | 保持关 |
| `#1125` 纯数据枚举 | GitHub **已关**；实现在 main | 保持关 |
| 分层物理化 | 架子有、墙没有 | 对照 `docs/audits/s14_layering_physicalization_design.md`，另开活 |
| BT/FSM 扳回 L2 | **开着** | 正轨见上；跟踪 PR #1416 |
| BT Parallel / BT-2 | 明确另线 | 别和扳回捆 |
| `FormatTextKey` / ActiveLocale / 生产 Dialogue drain | TextKey 后续 | 见 graph-textkey.md |
| 实体能力 authoring 声明与编译校验 | 编辑器下一切片 | 不得把运行时隐式安装写成已完成 |
| `LoadEntryPayloadText` | **合同缺口** | FormalText 已落地，入口捕获表尚无 String 槽 |
| trace 无时间 / 帧号 | **合同缺口** | 只许说齐亮齐灭 |
| `GraphDebugTool` 无自动化测试 | 债 | 仅有环形缓冲测 |
| 编辑器 lint 范围 / panel template 校验挂 | 债（非本轮） | 见既有说明 |
| `TriggerGraphRenameMigrationTests` 误伤合法 payloadKey | 债（非本轮） | 守卫应查字段而非裸子串 |
| 可调用函数远景（Case E） | **开着 · 先出方案** | [可调用函数远景](graph-callable-function-vision.md)；`NEXT-AGENT-BRIEF`；#1398 D1–D8 已勾完，远景另算 |
| `#1398` | 仍开 | 债务清单可关；远景留 vision / Case E PR |

分层合同条款同步修订在 [图怎么分层](graph-layering-flow-and-behavior.md)。

`#1125`（纯数据枚举）已关单且实现在 main：`Enums/enums.json` → `EnumCatalog`；`SwitchInt` / `SelectByEnum` 绑成员名；事件参数可注 `enumType`。不要再当「新开的线」派活。

下面这些早就知道、还没做，**不要当成新发现再审一轮**：默认「看见敌人 / 进入射程」还要有人先塞数字；图号在代码里还是普通整数；有一条事件丢弃计数永远是零；两个节点钉同一格时说不清。

### 3.4 不要当成这轮图能力去合

- 过期审计草稿，已经被后来的本子取代。卫生上该关，不是功能缺口。https://github.com/MightyBubble/Ludots/pull/961
- 打分预算是另一件事。打分短剧已经能玩，别和预算捆。https://github.com/MightyBubble/Ludots/pull/723
- 面板是另一条线。https://github.com/MightyBubble/Ludots/issues/886
- 助手工具无关。https://github.com/MightyBubble/Ludots/pull/947
- **不要**再开「把 BT/FSM 整树做成 Script 图糖」的实现票；正轨是 L2，扳回跟踪 PR #1416。

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
4. 我打开 Moba 演示。它已经能起来。#916–#918 实现在主干，票面只剩关单卫生。
5. 我问行为树 / 状态机怎么写。答案是 L2 拓扑资产，不是整树 Script 糖；糖路径是跑偏遗留。
6. 我打开工程。能看见两份薄契约。核心工程还是一大坨。总规矩第一行仍是修复中。

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

  Scenario: 行为树和状态机我知道写哪
    Given 我读完本页 §3.3.0
    When 有人问角色 AI 怎么画树、怎么画状态机
    Then 答案是 L2 拓扑资产加对应世界
    And 不是整棵树摊成一张 Script 糖图
    And 扳回进度指向 PR #1416
```

---

## 附录：对 GitHub 时再看

正文用不到这些编号。

已经合进主干：941、944、945、946、948、950、951、952、953、954、956、957、959、960、962、963、964、965、966、967、968。

已合进主干、只剩关单卫生：916、917、918（实现在 main，GitHub 若仍开就关）；1084、1099（已关）。

还开着、本页点过名的：915、861；BT/FSM 扳回跟踪 PR #1416；#1398 债务可关、远景留 Case E。

不要当这轮去合：961、723、886、893、947；也不要再开「整树 Script 糖当 AI 正统」的实现票。

本页和图基建收口：969。
