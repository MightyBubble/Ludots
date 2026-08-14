# 审计需求：main 图能力收口（#932 落地后）

**给审计 / 接手 Agent。**  
这是一次**独立架构审计请求**，对象是已经快进进 `main` 的图能力收口（落地 PR [#932](https://github.com/MightyBubble/Ludots/pull/932)，tip `82ddb3322a`）。  
不要只扫 diff 文件名；必须对照合同与玩家门做「说的和进游戏看到的是不是同一件事」。

本文件**不含结论**。结论另开一份 SSOT 报告。禁止借本需求夹带实现修复。

**刻意不审：** UI 面板图（#886 / #893 及查表/TagDisplay 面板债）、表现层改名/贴花/客户端座椅、更早平行的 GraphScore 预算 [#723](https://github.com/MightyBubble/Ludots/pull/723)。

---

## 1. 概述

图能力从「拆两本复用清单」走到「每个还能运行的图节点有一间能玩的展厅」，已经合进 `main`。产品要确认的不是「仓库变大了」，而是：

1. 玩家从启动器点的是登记表里的展厅，不是代码写死的默认关。  
2. 场上的人来自地图，血条走正式头顶条，字幕来自分镜配置。  
3. 纯算式不能偷偷挂起；可挂起动作不能嵌进效果事务。  
4. 覆盖表写 `covered` 时，测试和画廊都必须真的在。  
5. 合同若仍写「修复中」，报告里不得把它当成「已落地」。

| 对象 | 值 |
|------|-----|
| 被审 tip | `origin/main` @ `82ddb3322a` |
| 落地 PR | [#932](https://github.com/MightyBubble/Ludots/pull/932)（已合） |
| 合同 | `gitbook/architecture/graph-funclib-actionlib-contract.md`（状态：**修复中**） |
| 分层 | `gitbook/architecture/graph-layering-flow-and-behavior.md` |
| 覆盖表 | `assets/Configs/GAS/graph_node_op_coverage.registry.json` |
| 前序审计 | `docs/audits/pr911_funclib_actionlib_architecture_audit.md`（#914） |
| 修复清单 | `docs/audits/pr911_audit_fix_checklist.md` |

---

## 2. 结构

```text
阶段 0  对齐：读合同、钉产品共识、禁止重开已裁决争论
阶段 1  玩家门：启动器 / 登记表 / 分镜 / 生成器（先证伪「硬编码默认关」）
阶段 2  开图运行：GameEngine.LoadMap、空间索引、效果队列、失败关闭
阶段 3  库与作者：FuncLib / ActionLib / FrontDoor / 覆盖表
阶段 4  旁路残留：退役家族场、L2 叶子、技能热改
阶段 5  合成：一份 Verdict，禁止平行结论
```

领域（可并行，但阶段 5 必须合成）：

| 领域 | 一句话 |
|------|--------|
| A 玩家门 | 点哪一间、标题和预设从哪来 |
| B 分镜与生成 | 人/字幕/地图是否数据驱动 |
| C 开图与空间 | 无头是否走正式开图，圈人是否跟人走 |
| D 披露与血条 | 没亮的人是否还在、血条是否演戏 |
| E 复用库 | 纯算式 / 可挂起动作边界 |
| F 作者前门 | 文档能写的节点，作者是否真写得出 |
| G 覆盖表 | `covered` 是否假绿 |
| H L2 / 热改 | 巡逻叶子、火球改冰球是否仍走正式清单 |

交叉审计可用多个子 Agent，**最终只留一份**报告：

`docs/audits/pr932_graph_landed_architecture_audit.md`

---

## 3. 详情

### 3.1 产品共识（勿再争）

1. Duration / Period 在效果壳上；Effect 图内不用 Yield 冒充时间轴。  
2. FuncLib = 纯（无 Yield）；ActionLib = 可挂起；同名跨库失败关闭。  
3. Effect 可分支 + 调 FuncLib；不得调 ActionLib。  
4. 一种作者边模型 + 一台 VM；禁止平行编译器、平行程序宇宙。  
5. 图节点玩家门是 **单节点展厅**；八个家族大杂烩退役，不是第二套玩家入口。  
6. 人从地图刷；血条走 WorldHud / 生命披露；禁止 C# 改血演戏驱动 HUD。  
7. NO FALLBACK：缺清单、空表、未知符号、引擎空，全部失败关闭。

### 3.2 声称已交付（请逐项证伪）

**玩家门**

| 声称 | 关键路径 |
|------|----------|
| 每节点一间展厅，标题说人话 | `showcase.registry.json` 中 `capability_standard_graph_op_*` |
| 启动项是预设，不是默认写死 | `launcher.presets.json`、`launcher.config.json` bindings |
| 家族八条 `status=retired`，玩家 preset 已删 | 同登记表 + 生成器 `scripts/generate-graph-op-node-galleries.py` |
| 分镜 JSON 拥有人、字幕、featured 节点 | `…/GraphOpsNodeGalleryMod/assets/Vignettes/{Op}.json` |
| 薄入口 Mod 由生成器写出，禁止手改 | `mods/showcases/capability_standard/graph_op_entries/` |

**开图 / 空间 / 披露**

| 声称 | 关键路径 |
|------|----------|
| 无头走共享 `GameEngine` + `LoadMap` | `GraphOpsHeadlessGameEngine.cs`、`GraphOpsNodeGalleryHost.CreateHeadless` |
| 换展厅清残余人/效果/空间格 | `LoadExclusiveMap`、`SpatialQueryService.ClearPartition` |
| 圈人描边绑地图实体 | `GraphOpsStageVisuals.BindMapEntity` |
| 没亮的人世界血量仍在，HUD 不披露 | Knowledge 投影 / Health 通道；禁止 `ActorHealth` 演戏 |

**库 / 作者 / 覆盖**

| 声称 | 关键路径 |
|------|----------|
| 两本清单 + 失败关闭 | `func_lib.json` / `action_lib.json` + 两 Loader |
| 覆盖 120/120 且 `covered` ⇒ 画廊测试可解析 | `graph_node_op_coverage.registry.json`、`GraphNodeOpCoverageRegistryTests` |
| 合同状态「修复中」 | 合同首页；P3 仍有未勾项见修复清单 |

**已知未勾（核实是否仍开，勿当新发现）**

- 线性 `graphId` 拒绝 / Effect→ActionLib 名：FrontDoor 测试是否仍缺  
- 死 API `RequireId`、ScriptKeys 硬编码、加载顺序文档、旧 GraphCompiler 文档图  
- 合同在 P0–P3 完成前不得改回「已落地」

### 3.3 阶段 × 领域：给审计 Agent 的提示词

下面每块都可以**单独另开一个审计员**。纪律对所有块生效：只读自己工作区；先读 `gitbook/contributing/ai-assisted-development.md` 任务执行决策规范；NO FALLBACK / SSOT / 禁止发明 opcode；证据要有路径；不要重开产品争论。

---

#### 阶段 0 — 所有人先贴（短对齐，不写结论）

```text
你是只读审计员。对象：Ludots origin/main @ 82ddb3322a（#932 已合的图能力收口）。
先读并复述（各三句以内，禁止开始改代码）：
1) gitbook/architecture/graph-funclib-actionlib-contract.md 首页状态与三条边界
2) docs/audits/pr932_graph_landed_audit_handoff.md 阶段划分
3) 玩家门声称：单节点展厅是登记表驱动，不是启动器默认关
刻意不审：#886/#893 UI 面板、表现层改名、#723 GraphScore。
完成后停，等阶段任务。
```

---

#### 阶段 1 — 玩家门与数据驱动

**领域 A 启动器 / 登记表**

```text
阶段 1 / 领域 A。只审玩家怎么进图节点展厅。
对象：origin/main @ 82ddb3322a。
必读：launcher.config.json、launcher.presets.json、showcase.registry.json、
scripts/generate-graph-op-node-galleries.py。
证伪：
1) 启动器是否写死默认只开某一间图展厅？（应否：玩家从列表点 binding/preset）
2) capability_standard_graph_ops_* 八条是否 status=retired，且玩家 preset 已删？
3) capability_standard_graph_op_* 是否每条都有中文 title/summary、binding、preset、acceptanceTest？
4) 生成器是否是这些条目的唯一写入源？有无手改漂移（生成戳 vs 手工补丁）？
5) 退役家族 binding 是否仍出现在玩家可点的 preset 列表？
产出：阻断/Major/Minor 表，每条给文件路径。不要写总 Verdict。
```

**领域 B 分镜 / 地图 / 字幕**

```text
阶段 1 / 领域 B。只审「人、字幕、地图」是不是数据驱动。
对象：origin/main @ 82ddb3322a。
必读：CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/、
生成器写出的 gallery maps、graph_op_entries 薄入口、
覆盖表 graph_node_op_coverage.registry.json。
证伪：
1) 场上的人是否来自分镜/地图 actors，经 MapLoader 刷出？有无 World.Create 平行刷人当玩家路径？
2) 字幕/beat/assert 是否在 Vignettes/{Op}.json？C# driver 是否硬编码中文台词？
3) featuredNodeId 是否指向编译后的图节点，而不是 C# 猜寄存器？
4) 生成器声称「禁止手改入口 Mod」是否被手改破坏？
5) 任选 3 个不同族（浮点/圈人/关系）对照：title、地图人数、字幕断言是否同一份分镜？
产出：阻断/Major/Minor + 3 个节点的对照表。不要写总 Verdict。
```

---

#### 阶段 2 — 开图运行与披露

**领域 C 开图 / 空间 / 效果队列**

```text
阶段 2 / 领域 C。只审无头展厅是否走正式开图。
对象：origin/main @ 82ddb3322a。
必读：GraphOpsHeadlessGameEngine.cs、GraphOpsNodeGalleryHost.cs、
SpatialQueryService.ClearPartition、SpatialPartitionBackendBase.Clear、
GraphOpsNodeGalleryAcceptanceTests 及相关族测试。
证伪：
1) CreateHeadless 是否 SharedGallery + LoadExclusiveMap + FromEngine？有无平行 MapLoader/World.Create？
2) 换展厅是否清 EffectRequest、PresentationDestroyPending、空间格？不清是否会圈到上一间的人？
3) LoadMap 后是否真的等到 SpatialCellRef（固定步长，而不是 Tick(1/60) 不够一拍）？
4) 共享引擎是否第二次 GameEngine 导致 GraphIdRegistry Clear+Freeze、已绑定图丢失？
5) 图库地图没有 Boards 时，是否被错误当成失败？（声称：无 Board 合法，不要用 PrimaryBoard==null 一刀切）
产出：阻断/Major + 复现路径。不要写总 Verdict。
```

**领域 D 血条 / 知识披露 / 描边**

```text
阶段 2 / 领域 D。只审玩家看见的血条和描边。
对象：origin/main @ 82ddb3322a。
必读：GraphOpsStageVisuals.cs、画廊 performers.json、知识披露/Health 写入路径、
「没亮的人血条不投影」相关测试与提交说明。
证伪：
1) 血条是否走正式头顶条 + 生命披露？有无 C# ActorHealth / SetHealth 演戏？
2) 没亮的人是否还站在场上、世界生命仍在、只是 HUD 不披露？
3) 好感是否被偷偷写成血量？
4) 描边/圈人是否 BindMapEntity 跟人走，而不是 GraphOpsStageVisuals.Spawn 再造一套人？
5) 六角圈人：圈外的人世界血量是否仍是作者数据（声称满血 100），HUD 不披露？
产出：阻断/Major/Minor。把「演戏」和「真结算」分开写。不要写总 Verdict。
```

---

#### 阶段 3 — 库边界与作者前门

**领域 E FuncLib / ActionLib**

```text
阶段 3 / 领域 E。只审两本复用清单。
对象：origin/main @ 82ddb3322a。
必读：gitbook/architecture/graph-funclib-actionlib-contract.md、
assets/Configs/GAS/func_lib.json、action_lib.json、
GraphFunctionCatalogLoader、GraphActionCatalogLoader、GameEngine 加载段、
GraphYieldPurityValidator、docs/audits/pr911_funclib_actionlib_architecture_audit.md。
证伪（对照合同 §5，不是对照旧 #911 tip）：
1) 目录已声明但文件缺失 / 内容 [] → 是否仍失败关闭？
2) FuncLib 间接 Yield（含 graphId）加载期是否拒绝？热替换含 Yield 是否拒绝？
3) 非 Script FuncLib kind 是否加载拒绝？Score/Validation 延后是否名实相符？
4) ActionLib 与 FuncLib 同名是否失败关闭？Loader 的 FuncLib 参数是否必填？
5) Effect 能否间接调到 ActionLib / 含 Yield 的图？
6) 合同首页「修复中」与代码是否一致？有无假装「已落地」？
产出：对合同 §3/§5 逐条符合性。不要写总 Verdict。
```

**领域 F 作者前门**

```text
阶段 3 / 领域 F。只审「文档能写的，作者是否写得出」。
对象：origin/main @ 82ddb3322a。
必读：GraphProgramAuthoringFrontDoor、GraphControlFlowCompiler*、
graph-layering-flow-and-behavior.md、tag-display-lookup.md、
修复清单 P1 分族表、被恢复的守卫测试（SpatialQueryIncomplete 等）。
证伪：
1) 附录曾列的 44 个无前门 opcode，现在是否都有 FrontDoor 作者路径？
2) 线性 InvokeScript 是否只允许 functionName？graphId 直绑是否被拒？有无 FrontDoor 测试（清单 P3 曾未勾）？
3) Effect BranchBool 是否只是糖，Wait/While/Yield 是否仍禁？
4) 截断查询 / 不完整空间查询是否失败关闭？
5) 文档与真实前门字段是否双 SSOT？
产出：按族缺口表。不要写总 Verdict。
```

**领域 G 覆盖表**

```text
阶段 3 / 领域 G。只审 covered 是否假绿。
对象：origin/main @ 82ddb3322a。
必读：assets/Configs/GAS/graph_node_op_coverage.registry.json、
GraphNodeOpCoverageRegistryTests、showcase.registry.json、
GraphOpsNodeGallery* 测试。
证伪：
1) registry 成员集合是否 == GraphNodeOp 枚举？
2) 每条 status=covered 的 unitTestFilter 是否可解析且能跑通？是否含画廊测（声称含 GraphOpsNodeGallery* / EveryVignette）？
3) showcaseId 是否都在 showcase.registry.json 且 status=active（不是退役家族 id）？
4) 文案是否只堆 opcode 名？对照 title/summary/beat 是否说人话？
5) 生成器改 showcaseId 时有没有把未建成的展厅标成 covered？
产出：假绿清单（op / 缺测试 / 缺画廊 / 文案术语堆）。不要写总 Verdict。
```

---

#### 阶段 4 — 旁路残留

**领域 H1 退役家族场**

```text
阶段 4 / 领域 H1。只审八个家族 GraphOps Mod。
对象：origin/main @ 82ddb3322a。
路径：CapabilityStandardGraphOps{Float,Attr,Blackboard,Event,Script,Spatial,Rel,Query}Mod。
证伪：
1) 玩家入口是否已退役？测试是否仍把家族场当玩家门？
2) 可玩路径是否 engine.World + BindMapEntity？有无第二座 GameEngine 或 World.Create 刷展示人？
3) Spatial 是否误用 cellSize 再除一次导致锥/矩形为空？
4) Query/Rel 是否仍 GraphIdRegistry.Clear() 后平行绑库？
5) 家族 driver 未完工时是抛错还是假装能跑？
产出：残留表。家族场缺陷若不影响玩家门，标 Major/债务，不要自动升阻断。
```

**领域 H2 L2 叶子 / 技能热改**

```text
阶段 4 / 领域 H2。只审 L2 与火球改冰球。
对象：origin/main @ 82ddb3322a。
必读：GraphRegistryScriptResolver、*ScriptKeys、BT/HFSM/Level/ScriptFlow Showcase、
LiveGasEditPipeline、CommitNextCastSafeFrame、LswFireToIce 测试。
证伪：
1) L2 叶子是否 RequireActionId，有无 RequireId("Graph.…") 旁路或本地程序字典？
2) 是否至少一条 bt.* 真 Yield 且跨拍续跑？
3) 热改提交是否全有或全无？未知 Tag 是否 EngineRestartRequired（禁止热 Register）？
4) 火球改冰球是否仍走 Champion 正式施放管线，而不是视觉旁路？
5) #615 Save/UI 尾巴、Champion 真机录屏债：记录为既有债，不扩写成本次唯一阻断，除非回归合同红线。
产出：符合性 + 债。不要写总 Verdict。
```

---

#### 阶段 5 — 合成（只允许一个 Agent）

```text
阶段 5。你是唯一合成员。收集阶段 1–4 各领域表，只写一份报告：
docs/audits/pr932_graph_landed_architecture_audit.md
并在 docs/audits/README.md 加目录链接。

报告必须有：
1) Verdict：HOLD MAIN / FIX-FORWARD / REGRESS（禁止用 MERGE，因为已经在 main）
2) 阻断 / Major / Minor / 债务表（路径 + 证据）
3) 玩家门结论先写：是不是硬编码默认关；人/字幕/血条各从哪来
4) 合同 §3/§5/§6 符合性；合同状态该不该继续「修复中」
5) 与 #914 审计的衔接：哪些已关、哪些仍开、哪些是新债
6) 给修复 Agent 的最短提示词（按领域拆，不要一条巨提示词）

禁止：多份平行结论；夹带实现；重开产品争论；把 UI 面板/#723 写进阻断。
纪律：NO FALLBACK、SSOT、说人话写场景证据。
```

---

### 3.4 建议报告骨架

```text
# 1. 概述（Verdict + 玩家一句话）
# 2. 结构（阶段/领域对照）
# 3. 详情（表）
# 4. 场景（玩家看见什么 vs 实际）
# 5. 边界（不审项、已裁决共识）
# 6. UAT（对照下面 §6，标过/未过/无法测）
```

---

## 4. 场景

审计时用玩家/作者眼睛，不要用架构名词当场景。

1. 打开启动器：能看到很多展示；图节点是一长串中文短剧名，不是「默认进了某一关」。  
2. 点「两段伤害叠在一起」：场上有施法者和木桩，字幕说两段伤害相加，血条按总和掉。  
3. 再点一间圈人展厅：上一间的人不应还站在圈里被数进去。  
4. 没被点亮的人还在场上，头顶条不投影；世界生命不是被 C# 改成演戏用的数。  
5. 策划删掉动作清单或写成空表：启动就失败并点名路径。  
6. 纯算式里跳进「等一拍」：登记/热改当场拒绝。

---

## 5. 边界

**做**

- 证伪 `main` 上图能力收口的玩家门、开图、库边界、覆盖表、退役家族残留  
- 对照合同与 #914，写清仍开的债  
- 一份 SSOT 报告

**不做**

- 改生产代码、顺手修 UI 面板、重开 Duration/Yield 产品争论  
- 把 Champion 录屏债、#615 Save 尾巴、#723 评分预算升成本次唯一目标  
- 用「合同缩编」或改测试门槛掩盖分配/耗时  
- 发明新 graph op / profile enum / 第二套加载器

**已知已知（核实，勿当新发现）**

| 项 | 说明 |
|----|------|
| 合同「修复中」 | 修复清单 P3 仍有未勾项；不得在报告里写成已落地 |
| 旁路票代码已叠进 main | 护盾 source 口、GasTests 拷贝排除、数学链 0-alloc：核实现状，不要按旧红灯复读 |
| GitHub issue #915–#918 | 关单权限当时未落到落地身份；票面开着不等于代码没进 |
| docs-governance | #932 合入时校验曾红；若仍红，记 Major 并点名规则，不要只写「CI 红」 |

---

## 6. UAT

```gherkin
Feature: 启动器不是写死的图展厅
  作为玩家
  我希望打开启动器时先看到一列展示
  以便我自己点进铁匠铺、技能热改或某一间图节点短剧

  Scenario: 列表里有很多门
    Given 我启动的是 main 上的启动器
    When 我还没有点任何一项
    Then 游戏不得自动只进某一间图节点展厅
    And 我能看到图节点短剧的中文标题
    And 退役的家族大杂烩不得再作为可点的玩家入口

Feature: 一间展厅是一幕短剧
  作为新玩家
  我希望点进「两段伤害叠在一起」时看见人和血条
  以便我看懂这一刀为什么掉那么多血

  Scenario: 分镜说了什么，场上就是什么
    Given 分镜写了施法者和木桩以及一句中文字幕
    When 我点进对应展厅
    Then 场上的人来自这张地图而不是代码里另造的一批
    And 字幕来自分镜而不是驱动里写死的台词
    And 血条跟着正式生命走，不是脚本把血改成演戏数字

Feature: 换一间展厅不能把上一间的人留下来
  作为玩家
  我希望连看两间圈人短剧时人数对得上
  以便我不会觉得引擎在作弊

  Scenario: 上一间的人消失
    Given 我刚看完一间六个人的圈人展厅
    When 我再点进另一间只要圈内六人的展厅
    Then 场上不该还站着上一间的人
    And 字幕报的人数必须和我看见的亮着的人一致

Feature: 纯算式不能偷偷等一拍
  作为技能作者
  我希望纯算式清单里的图不会让结算跨拍
  以便技能不会做到一半停住

  Scenario: 间接挂起被拒绝
    Given 一张图自己不含挂起
    And 它引用了会等一拍的动作
    When 我把它登记进纯算式清单
    Then 登记必须失败并说明原因
```

---

## 7. 必读（最短）

- 本文件  
- `gitbook/architecture/graph-funclib-actionlib-contract.md`  
- `gitbook/architecture/graph-layering-flow-and-behavior.md`  
- `docs/audits/pr911_funclib_actionlib_architecture_audit.md`  
- `docs/audits/pr911_audit_fix_checklist.md`  
- `scripts/generate-graph-op-node-galleries.py`  
- `GraphOpsHeadlessGameEngine.cs` / `GraphOpsNodeGalleryHost.cs` / `GraphOpsStageVisuals.cs`  
- `gitbook/contributing/ai-assisted-development.md`（任务执行决策规范）

---

## 8. 本需求文档的范围

- **包含**：上下文、阶段/领域提示词、产出格式、UAT。  
- **不包含**：审计结论、实现修复。  
- **禁止**：多份平行报告；把提示词合成一条让一个人同时审所有领域。
