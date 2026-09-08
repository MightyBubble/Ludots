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

**编辑器里程碑（控制流与 live debug 已收口；正式文字合同已齐）。**
节点联想只从运行时 descriptor 获取；Bridge 投影作者糖及其控制/值端口（含 `BranchBool`、`SwitchInt`、`SelectByEnum`、`FsmState`、`Wait`、`While`、`Until`、`Break`；Script 另有 `BtSequence` / `BtSelector` / `BtDecorator` 与动态 `child:{n}`；TriggerGraph 另有 `InlineGraph`）。`Jump.target`、`Call.call/next` 等普通控制端口也来自 Bridge descriptor，React 不维护第二份 op 端口表。`Break` 编译时严格降低为带显式 `target` 边的 Jump；`Select` 仍明确是实体选择 `SelectEntity`，不是尚不存在的通用 Select。编辑器连线、删节点后的悬挂边清理、布局数据校验和 live trace source map 校验均走失败关闭。地图变量面板只暴露 Integer / Float，不再列出引擎还不认的 Array / Map。

Live debug 记录实际执行节点归因、Yield/预算挂起、Halt、游标、引脚和黑板变化；嵌套 `InvokeScript` 继承固定容量 trace 并携带子图 id。编辑器侧按 Flow Canvas 方式点亮节点/控制边并贴 pin 芯片，`drain` 事件带 `controlPort`；当前不伪造 `NodeExit` 生命周期事件。黑板 buffer 缺失仍在运行时明确失败；实体能力在 authoring 阶段的声明和编译校验仍是下一条合同切片，不能把运行时隐式安装路径写成已完成。

Epic #1464 的 A1 在 PR #1466 中交付两段，尚未合入：trace 记录区按需分配（验收见 `artifacts/acceptance/triggergraph-trace-memory/`）；寄存器、目标、调用栈、入口载荷与调用参数集中到固定容量执行槽。开始运行领槽，Yield/回调等待保留，完成、错误和注销归还。同步嵌套事件使用独立槽，结束后恢复外层；嵌套运行跨帧等待明确失败。`assets/game.json` 的 `triggerGraphExecutionCapacity` 默认 1024，约束同时活动的运行数，Mod 经原配置管线覆盖；0 或负数启动失败，耗尽报 `GRAPH.EXECUTION.ERR.CapacityExceeded`。`ludots.graph.debug list` 返回 `executionSlots.capacity/inUseCount/highWaterMark`，逐挂载仍有 trace 的 `capacity/allocatedCapacity`。

执行槽定向测试 47/47；10,000 个完整挂载及恢复伴随触发器的发布版批次，在预热后开始和恢复新增分配为 0，槽占用 10,000 → 0。此次单跑开始/恢复分别为 17.978/31.320 ms，未计入事件总线、实体过滤、完整引擎帧和渲染，不是万人帧率验收。BT/HFSM 单独验收 20/20，合波 p95=4.844 ms；与编译并行时曾出现 p95=17.014 ms 的超限，保留在输出中。扩大回归还有两项在干净 main 同样复现的断言失败（旧命名检查、GraphReturnWriter 错误文案），不列为本轮新发现。最终夜袭真机有 9 个挂载，活动槽在事件前后均为 0、峰值 1；调试开关和 18 条轨迹通过。证据见 `artifacts/acceptance/triggergraph-execution-slots/`。A1 总项仍待完整万人场景帧时间及交互展示验收，A2–A9 其余工具链任务继续由 #1464 跟踪。

底栏用人话讲这一趟，数据是 mod 自己的：`mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graph_editor.json` 的 `annotations`（节点分组每图声明一次 + 按入口写抬头），Bridge 读写都对着 `graphs.json` 核对分组节点与入口标签，改名失败关闭并点名。底栏按执行到达顺序列出走过的每一组，和画布热度同一个 TTL 一起冷掉。编辑器源码里不得出现具体图 / mod / 节点 id，`ReactEditor_MustNotNameShowcaseGraphsOrMods` 扫全前端目录守这条。入口起因是「等事件」或「等输入动作」的单选，运行时 `event` / `action` 恰有其一；动作 id 从 `/api/graph/input-actions/{modId}` 合并目录下拉，保存路径跑 `RequireTriggerGraphEntryShape`。编辑器前端已进 CI（`graph-editor-frontend`：tsc + 图编辑器目录 lint + 断言脚本）。

trace 记录只有序号和步数，没有时间或帧号：一拍跑完的链是齐亮齐灭，不是逐步流动。要真做流动，先给记录补时间或帧号，别在文档里先许诺。

字符串花括号自动引脚、字符串寄存器、组合文本与 `Concat` 的运行时合同已落地：`GraphValueType.Text` + 固定容量 `GraphTextHeap`、`ConstText` / `ConcatText` / `IntToText` / `FloatToText` / `SinkPresentationText`，以及作者糖 `FormatText`（花括号自动引脚，编译期降为原子文字 op）。合同正本见 [图正式文字](graph-formal-text.md)；作者接法见 [拼句指南](graph-formal-text-authoring-guide.md)。玩家短剧「拼一句上字幕」见 [验收](../acceptance/graph-formal-text-subtitle.md)（`capability_standard_graph_formal_text`）。编辑器只从运行时 descriptor / 已登记糖露出可保存节点，不再留假 Concat。

TextKey 发现糖（Tag 式选键 → 真 i18n catalog）与 FormalText 字面量轨分离：可保存 op `LoadTextKey`、Bridge `/api/graph/text-keys/{modId}`、编辑器 `textKey` 选择器。合同正本见 [图 TextKey 发现糖](graph-textkey.md)。本切片零参；带参 `FormatTextKey`、ActiveLocale 对齐、生产 Dialogue drain sink 另线。

**图 Codegen 产品化（CG-0…CG-6 + 运行时装载已落地）。**  
正式程序集 `Ludots.Graph.Codegen`：F0–F3 特化发射（允许回边），其余家族 HandlerForward；coverage 全量 `covered`；Bridge 预览/对拍/覆盖；编辑器 Codegen 面板。运行时：`game.json` 键 `graphExecutionBackend`（`interpret` / `codegen` / `codegen-prefer`）在装图后绑定生成入口；`GraphExecutor` 优先走生成码；`ludots.graph.debug` 与 Live Debug 标题报 `executionBackend`。夜袭旗舰 `graphExecutionBackend=codegen`。合同正本 [图 Codegen 产品化](graph-codegen-productization.md)；自审 `artifacts/gas-composition-gate-graph-codegen-impl.md`。未知 op / 绑定失败在 `codegen` 模式失败关闭。

作者面状态：执行线结束合同票 https://github.com/MightyBubble/Ludots/issues/1107 已关闭，不再列为开放任务；当前显式 Halt 合同以编译器和回归为准。蓝图变量面板 MapVariable 作者面已随 Narrative PR #1222 / Bridge 进主干，#1109 已关单。#1108 要对齐的是「地图上具体 InstanceId（单位/区域）当变量拖取」——单实体 `LoadPlacedEntity` + 区域 `LoadPlacedRegion` + 锚点 `LoadPlacedAnchor`（InstanceId 含 `anchor`）+ Placed 栏 / Bridge `kind` 已落地；不是数组/映射集合类型。事件入口露出本次载荷（#1106）、放置实体读、地图变量变更事件（#1113）、图互调/跨图派发/全局订阅与 hook（#1115/#1116/#1123/#1124）、纯数据枚举（#1125）、图↔代码 AwaitCallback 续跑（#1126）已随 night-raid 大包进主干（PR #1239）；对应票（#1106/#1113/#1115/#1116/#1123/#1124/#1125/#1126，连同随 #1222 落地的 #1109、随 TriggerGraph core 线落地的 #1114）已于 2026-08-28 做关单卫生关闭，不要再派实现票。#1126 落地范围：`AwaitCallback=455` + `GraphCallbackService` + `SystemGroup.Continuation` 按注册序 Drain；TriggerGraph 挂载可直接挂起；嵌套 `InvokeScript`/`InvokeGraph` 仍禁 Yield/AwaitCallback（同步函数）。可等待复用走编译期糖 `InlineGraph`（`TriggerGraphInlineWeaver`，虚幻 Macro 风格，Await 落在宿主程序）。Dialogue 宿主 Completer 已接线：玩家确认选项/推进台词时 `TryCompleteByCallbackType(DialogConfirm)`，不另造第二套等待。进图开聊的正式入口已落地：`StartDialogue=462`（PR #1289，对话作者关口入门包）——`MapLoaded` TriggerGraph 起聊，`dialogue_author_kit` 展厅纯配置可玩；per-op 画廊真机证据（poster/play.mp4）已补录。未完成前，编辑器不得画出保存后引擎不认的假针脚或假集合。

**分层：架子有了，墙没有。**  
工程里多了两份薄的契约，核心工程还是一大坨。展厅大多还能一把抓住整台引擎。把空间、输入、画面、结算真正拆开，以及不许再抓整台引擎，这两步没做。要做就单独开活，对照 `docs/audits/s14_layering_physicalization_design.md`，别和修演示、修构建捆在一起。没拆完之前，总规矩继续写「修复中」。

**两张总账先别关。**  
每个图节点都要能写、能测、能看见：https://github.com/MightyBubble/Ludots/issues/915  
作者只走一条边、只进一扇门：https://github.com/MightyBubble/Ludots/issues/861  
画廊和一批门已经合了。三张旁路票只剩关单，分层没拆完，所以总账还开着。

新开了一条线，别当成图能力收口的回锅：触发器图（TriggerGraph，原 MapTriggerGraph）。
进度与计划只认两张票：地图域线 https://github.com/MightyBubble/Ludots/issues/1030 ；域扩展线（实体域挂载、GAS 事件桥、技能/效果时刻桥、presenter 时序合同）https://github.com/MightyBubble/Ludots/issues/1031 ——两张票顶部各有进度快照与剩余切片清单，新活从快照开工，别重做已落地的。
方言/挂载、事件词典（MapHeartbeat 地图心跳/实体死生/区域）、地图变量存储、时间线续跑、实体域挂载、GAS 桥、「夜袭三波」全数据旗舰与旧 LevelDirector 试验线退役，都已落地；2026-08-24 又补上技能域 `abilities.json.triggerGraphs`、Mod 域 `mod.json.triggerGraphs` 和显式 `route: global` 跨地图路由，统一复用现有 TriggerManager/TriggerGraph VM。2026-08-26 night-raid 大包（rebase 最新 main，PR #1239）继续把事件 Schema SSOT、全局订阅表/`FireGlobalEvent`/`FireCrossMapEvent`、图互调与放置实体读、Enum 目录、图编辑器作者面 hardening 收进同一条线；真正的跨图派发走 `FireGlobalEvent`，不再靠 FireMapEvent 扇出旧表。区域触发源 2026-09-07 起改为实体 SSOT（#1461）：作者面走既有摆放流水线——实体模板 `components` 声明 `RegionVolumeCm`（circle/rect/凸 polygon/segment 四种形状）+ 可选 tag 过滤与 `RegionVolumeEmissionCm` 发射合同（自定义事件 + 静态 payload），地图 `Entities[]` 摆放、`Overrides` 整组件替换改形；摆放后 bake pass 做 emit 语义校验（对 CustomEventNameRegistry/EventSchemaRegistry fail-closed）并派生 VolumeKey 目录，`RegionVolumeTriggerSystem` 在心跳节拍做差分进出求值，默认事件键与 payload 合同不变。地图 JSON `Regions` 数组、MapRegionDefinition/RegionTriggerSystem 字典版、`PlacedInstanceKinds.Region` 特例全部退役；夜袭与 LoadPlacedRegion 画廊已迁移到模板+摆放（摆放 `PositionXCm/PositionYCm` 兜底落 WorldPositionCm 是引擎级新能力）。FieldRegion 事件族合并与物理 sensor 收敛是后续票（#1468/#1469）。图体一次性控制流补齐 UE 同款 DoOnce 编译期糖（#1467）：var 闩锁 0→1、true/false 双臂、Reset≡WriteMapVarInt(var,0)，降级为 Read/Compare/JumpIfFalse/Write 链零新 opcode；入口级 once 保留为关卡导演糖（17 个生产图在用），两者分工已写进 #1467。高速穿掠防漏判由扫掠判定补齐（#1475）：评估波内对区外移动者做 [PreviousWorldPositionCm → 当前位置] 轨迹求交（跨立测试 + 端点距离，全定点有界无溢出），瞬时穿越同波发 enter+exit 配对且不占 inside-set；无 Previous 组件回退点采样（合同不变）。剩余收口是 S4 时序合同全文对齐与 S5 实体/技能真实可玩 showcase、画廊和 AgentBridge 运行证据，不能把 headless 基建测试写成 showcase 完成。图侧 spawn 动词已经落地：SpawnTemplate（GraphNodeOp 447）在 TriggerGraph 与 Script 都能用，「夜袭三波」旗舰的 stage3 就用它在图内生成 boss（`mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graphs.json` 的 `spawn_boss` 节点）。合不合、什么时候合，看 #1031 的最新进度快照。

又开了一条线：行为树「真图化」（BT-1）与 HFSM「真图化」（FSM-1）。设计冻结本在 `artifacts/showcases/graph-fsm-bt-refactor-design.md`（L2 身份已纠偏，见下）。

**BT / FSM 作者合同（已纠偏）：** 外层是 L2 拓扑，不是 Script 糖文档。BT SSOT = `AI/behavior_trees.json` → `BehaviorTreeWorld`；FSM SSOT = `AI/hfsm.json` → `HfsmWorld` + `GraphProgramHfsmHost`；叶子 = `action_lib.json` + `GAS/graphs.json` Script。编辑器正门：`/bt-editor` / `/fsm-editor` 写 AI JSON（Bridge `GET/PUT /api/ai/behavior-trees|hfsm`），双击叶子进 `/gas-graphs`。合同正本 [BT/FSM 独立编辑器与函数图叶子](graph-bt-fsm-nested-func.md)。

**糖 / 降级宿主（回归，非作者 SSOT）：** `BtSequence` / `BtSelector` / `BtDecorator` / `FsmState` 与 `GraphBehaviorTreeHost` / `GraphFsmHost` 仍保留作编译降级与单元回归；**禁止**再把整树 / 整机 Script 糖当作演武场或编辑器正门。生产资产已删除 `Graph.BT.Tree.PatrolChaseAttack` / `Graph.FSM.Sentry` 外壳。

**演武场：** BT arena featured = `bt.patrolChaseAttack` + ActionLib 叶子；crowd = 无图 `bt.arenaCrowd`（`ScriptSlices==0`）。哨兵 arena featured = `hfsm.sentry.scripted` + 叶子 Script；crowd = 无图 `hfsm.sentry`（`LifecycleRuns==0`）。整合演示同走 L2 拓扑宿主（不再标成「不得顶旗舰」的旁路）。

还开着的（**另开活，本轮别捆**）：Parallel（一期显式不支持）、子树复用/异步叶（BT-2）。
### 3.3.1 图相关还开着的（勿当新发现重审）

Case E 查询债务施工：分支 `codex/case-e-query-completeness` 已实现完整收集、派生集合绑定和空间框选。后续整合进入 PR #1473：万人场景提供玩家切换、独立选择名单及蓝环/黄环动态隐藏。PI 双路命名复审后，同步 main `09705ec204`，GAS 门禁、Case E 与区域回归 808/808，Presenter 专项 26/26；完整注释配置随生产配置更新。此前 Presenter 扩展回归 269/270，剩余 1 项为原性能分支同样失败的错误文字断言。设计见 `artifacts/techdebt/2026-09-07-case-e-selection-query-design.md`，最新配置、实机证据、性能与边界见 [Case E 整合报告](../../artifacts/acceptance/case-e-consolidation/REPORT.md)。任意谓词、完整 ECS 查询编译、集合重新物化和首次万人表现创建仍有边界，债务保持开放。本轮独立于 #1456。

| 项 | 状态 | 怎么开工 |
|----|------|----------|
| `#1107` 执行线结束合同 | 已关闭 | 不再派实现票；当前合同见编译器与回归 |
| `#915` 每节点可写可测可看 | 总账开着 | 旁路已合，别当实现票重做 |
| `#861` 作者只走一条边 | 总账开着 | 同上 |
| 分层物理化 | 架子有、墙没有 | 对照 `docs/audits/s14_layering_physicalization_design.md`，另开活 |
| `#1031` S4 时序全文 / S5 实体技能可玩 showcase | 域扩展剩余 | 看 #1031 进度快照 |
| BT Parallel / BT-2（子树复用、异步叶） | 明确另线 | 冻结本；别捆 BT-1 |
| `FormatTextKey` / ActiveLocale / 生产 Dialogue drain | TextKey 后续 | 见 graph-textkey.md |
| 实体能力 authoring 声明与编译校验 | 编辑器下一切片 | 不得把运行时隐式安装写成已完成 |
| `LoadEntryPayloadText`（事件 String 载荷进 Text 寄存器） | **合同缺口** | FormalText 已落地，但入口捕获表尚无 String 槽；编辑器对 String 针脚返回空 |
| 外层 L2 拓扑 SSOT 恢复（AI JSON + 拓扑编辑器） | **已落地** | 见 [BT/FSM 独立编辑器](graph-bt-fsm-nested-func.md)；糖宿主仅回归 |

| trace 记录没有时间 / 帧号 | **合同缺口** | 想要真的逐步流动就给 `GraphDebugTraceRecord` 补时间源；在那之前只许说齐亮齐灭 |
| 编辑器前端 lint 只门到图编辑器目录 | 债 | `StoryAuthoringPage.tsx`（10 处 `no-explicit-any`）与 `ui-panel-authoring/model.ts`（1 处未用变量）先欠着，清完再放宽 `graph-editor-frontend` 的 lint 范围 |
| `npm run check` 末步 `validate-panel-templates` 本来就挂 | 债（非本轮） | 报 `Unsupported schema 'ludots.ui.panel_template'`，main 上同样挂；`graph-editor-frontend` 不跑这步，属面板线 |
| `TriggerGraphRenameMigrationTests` 误伤合法 payloadKey | 债（非本轮） | 夜袭 `graphs.json` 的 `MapTrigger.PointerScreenX/Y`（随 #1398 入口直绑 action 落地）被「不得出现退役方言名」的子串检查判红；该守卫要改成只查 `kind` / `mount` 字段而不是裸子串 |
| `QueryAllMapEntities` 定长 TargetList（`MaxTargets=256`）全图查询截断 + roster 候选集「全量重建式」无增量维护原语 | **P1 债 · 做法要变** | 债务正本 `artifacts/techdebt/2026-09-07-case-e-selection-query-cap.md`；可复现/观测 `CaseESelectionScalePressureTests` + `docs/benchmarks/case-e-selection-scale/`。两条线：①解除 256 顶（分页/流式收集，图 VM 寄存器模型重新设计——query graph 专属编译管线适配 ECS 流式查询的裂缝）；②候选集从「事件驱动全量重建」迁到「增量成员 + 过滤条件 diff」（并为 team/模板/状态突变补刷新源）。**另开活，别捆本 PR** |
| 可调用函数远景（Case E：入参表、whileActive（已替 continuousQuery）、预览 S1/S2、Invoke 与 FuncLib） | **开着 · 先出方案** | 正本 [可调用函数远景](graph-callable-function-vision.md)；Case E 短任务条 `mods/showcases/case_e_selection/CaseESelectionMod/docs/NEXT-AGENT-BRIEF.md`。PR #1444 是台阶。评审前不大改 Core。 |


分层合同条款同步修订在 [图怎么分层](graph-layering-flow-and-behavior.md)。

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
