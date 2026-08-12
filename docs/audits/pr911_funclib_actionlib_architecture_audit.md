# PR #911 架构审计：FuncLib / ActionLib 拆分与 Effect 表达力

**审计对象：** PR #911（`cursor/funclib-actionlib-impl-45dc`）  
**审计 tip：** `20bf1e031`（`docs(architecture): mark FuncLib/ActionLib contract as landed`）  
**基线：** #895 head `cursor/bc-0bbd794f-c30a-4fd5-8b62-2ece111a5e55-79e6`（含 #909 cherry-pick）  
**交接 SSOT：** [`pr911_funclib_actionlib_audit_handoff.md`](pr911_funclib_actionlib_audit_handoff.md)（PR #912）  
**合同对照：** `gitbook/architecture/graph-funclib-actionlib-contract.md`、`gitbook/architecture/graph-layering-flow-and-behavior.md`  
**前置审计：** [`pr895_graph_infra_and_lsw_architecture_audit.md`](pr895_graph_infra_and_lsw_architecture_audit.md)（#906）

---

## 1. 概述

按交接做「产品边界是否闭合」审计，不是扫文件名。  
三路只读交叉（库边界 / Effect 表达力 / 旧编译器+L2+栈）后，审计人复读阻断与 Major 证据。

**合并结论：禁止合入（DO NOT MERGE）。**

主线交付（ActionLib 资产与加载器、FuncLib purity/禁 Yield、Effect `BranchBool`+线性 `InvokeScript`、L2 Showcase 改绑 ActionLib、删除 next-chain `GraphCompiler`）在**有文件、有条目、按名字调用**的幸福路径上大体成立；但合同 §5「缺文件/缺条目不得静默空表」未闭合，文档却标「已落地」；Arena 热路径门槛被无依据放宽；Query 仍不能调 FuncLib；作者/玩家 UAT 与资产名漂移。合入前必须先关阻断并降级或兑现「已落地」声明。

| 主线 | 结论 |
|------|------|
| Catalog 拆分 + 引擎加载顺序 | **PARTIAL**：顺序与 Script-only/同名冲突成立；缺文件/空数组静默空表 = 阻断 |
| Effect / 线性 Kind 表达力 | **PASS（切片）**：Effect 可 BranchBool + FuncLib；Wait/While/Yield 仍禁 |
| L2 Showcase 零旁路 → ActionLib | **PASS（绑定）**：CapabilityStandard 叶子走 `RequireActionId` |
| 旧 next-chain 编译器退役 | **PASS**：`src/` 无 `GraphCompiler`/`GraphValidator`；FrontDoor/Editor 硬拒 `nodes[].next` |
| 合同「已落地」名实 | **FAIL**：§5 静默空表、§3.3 Query、§6 UAT 映射与命名未对齐 |
| 与 #906/#909 | B1/B2/M1 等 #909 修复仍在分支；FuncLib 静默空表债未关且扩到 ActionLib |

---

## 2. 结构

```text
1 概述 / 合并结论
2 结构（本页）
3 详情：方法、符合性、缺陷清单、#906 衔接
4 场景：作者/玩家会撞到什么
5 边界：覆盖与不覆盖
6 UAT：合入前必须成立（Cucumber）
附录 A 交叉审计过程
附录 B tip / 范围
附录 C 给修复 Agent 的最短提示词
```

---

## 3. 详情

### 3.1 审计方法

1. 读交接全文、合同、#906 报告与任务执行决策规范。  
2. 三路只读：  
   - A：FuncLib/ActionLib 加载、同名、Yield、Effect 调用路径  
   - B：Effect/线性 Kind 表达力与命名选择  
   - C：旧编译器残留、L2 Showcase、Arena 门槛、栈与 #909  
3. 审计人对阻断/Major 源码逐段复读（Loader、`ConfigPipeline`、Linear/Query 编译器、Arena 测试、Showcase Detail、#909 Commit 回滚）。

### 3.2 合同符合性（§3 / §5 / §6 摘要）

| 条款 | 结果 | 证据 |
|------|------|------|
| §3.1 Duration/Period 不用 Effect 内 Yield | **PASS** | Effect 前门拒 Wait/Yield/While/Until（`GraphEffectAuthoringExpressivenessTests`） |
| §3.2 Score/Validation/Derived 可调 FuncLib、拒 BranchBool | **PASS** | 线性白名单含 `InvokeScript`；`IsBranchBoolAuthorable` 仅 Script/Effect |
| §3.2 Query 准纯 + 可调 FuncLib（§3.3「所有 L1」） | **MAJOR** | `GraphControlFlowCompiler.Query.cs` 白名单**无** `InvokeScript` |
| §3.3 FuncLib purity + 禁 Yield | **PASS（加载器）** | `GraphFunctionCatalogLoader` L55–L92 |
| §3.3 调用只用一套作者名 | **PASS（实现选择）** | 保留 `InvokeScript.functionName`；无并行 `InvokeFunc` 节点 |
| §3.4 ActionLib Script-only、禁与 FuncLib 同名 | **PASS（生产路径）** | `GraphActionCatalogLoader`；`GameEngine` 传入 FuncLib |
| §3.4 Effect 不得 InvokeAction | **PARTIAL** | 线性强制 `functionName`→只走 FuncLib patch；无 `InvokeAction` opcode；缺专用前门测试与明确诊断文案 |
| §3.5 Effect BranchBool + FuncLib | **PASS** | Linear + `IsBranchBoolAuthorable` + 表达力测试 |
| §5 缺文件/缺条目禁止静默空表 | **BLOCKER** | `ConfigPipeline` 吞 `FileNotFoundException`；Loader Clear 后空 merge 仍成功 |
| §5 Effect 禁 Yield/InvokeAction；禁平行 VM | **PASS（名路径）** | 见上；`graphId` 嵌套洞见 Major |
| §6 Cucumber 玩家/作者场景 | **MAJOR** | 单元测覆盖表达力子集；Showcase Detail 偏术语；`bt.patrolStep` 与资产 `bt.patrol` 漂移；BT/HFSM ActionLib 图多为无 Yield 桩 |

### 3.3 阻断（Blocker）

| ID | 缺陷 | 证据 |
|----|------|------|
| **B1** | catalog 已声明 `GAS/func_lib.json` / `GAS/action_lib.json` 时，VFS **缺文件**或 merge 得 **空数组**，Loader 仍 `_catalog.Clear()` 后成功返回 **Count==0**——违反合同 §5「禁止缺文件/缺条目静默空表」，亦违反交接 A.4 | `ConfigPipeline.cs` L202–L205（`catch (FileNotFoundException) { /* Ignore missing files */ }`）；`GraphFunctionCatalogLoader.Load` L36–L44；`GraphActionCatalogLoader.Load` L33–L43；`RequireEntry` 只要求 catalog **声明**路径，不要求文件存在 |

复现意图（修复后须红→绿）：

1. 保留 `config_catalog.json` 中两条 lib 声明。  
2. 从 VFS 去掉 `action_lib.json`（或写成 `[]`）。  
3. 跑生产加载路径：`GameEngine` → `GraphActionCatalogLoader.Load`。  
4. **期望：** 失败关闭并指出缺文件/空表。  
5. **现状：** 空 `GraphActionCatalog`，引擎继续启动；直到 L2 `RequireActionId` 才炸——边界在加载期未闭合。

> 注：#906 已将 FuncLib 同款静默空表记为 Major/合入前清单第 7 项；本 PR 把同一模式复制到 ActionLib，且合同正文已把该条写入 §5，同时文档状态写「已落地」——故升格为阻断。

### 3.4 Major

| ID | 缺陷 | 证据 |
|----|------|------|
| **M1** | Query Kind 前门不能 `InvokeScript`/FuncLib，违反 §3.3「所有 L1 Kind 前门白名单必须包含该调用节点」 | `GraphControlFlowCompiler.Query.cs` L9–L38（无 `InvokeScript`） |
| **M2** | Arena CI 门槛：avg **5→20ms（4×）**、p95 **5→40ms（8×）**；方法名仍 `StayUnderFiveMs`；注释归因 ActionLib bootstrap，但 bootstrap 在计时器外，计时路径为拓扑-only（AlwaysSuccess / Sentry / Level，叶子脚本未跑）——可掩盖拓扑回归 | `GraphBehaviorArenaAcceptanceTests.cs` L18–L95；相对 `main` 的 diff；commit `28c94eefe` |
| **M3** | Script 非线性能 `InvokeScript.graphId` 直绑任意已注册 Script，绕过两库；嵌套在 FuncLib Script 内时可把 **无 Yield** 的 ActionLib 图拖进 Effect 事务（运行时只拒 Yield） | `GraphControlFlowCompiler.cs` L1310–L1338；`GasGraphOpHandlerTable.HandleInvokeScript` Yield 检查；当前 ActionLib 多数叶子本身无 Yield（桩） |
| **M4** | `GraphRegistryTestBootstrap` 手写读 JSON `Register`，**不走**生产 Loader 的 purity/Yield/Script-only/空表门禁；依赖 bootstrap 的「核心资产迁移」测试给出虚假信心 | `GraphRegistryTestBootstrap.cs` L24–L48、L91–L110 |
| **M5** | 合同 §6 / Showcase 验收未从玩家视角闭合：Detail 堆「BT Script / HFSM / ActionLib」术语；Cucumber 写 `bt.patrolStep`，资产是 `bt.patrol`；BT/HFSM ActionLib 图基本是 `HaltReturnInt` 桩，**未演示**「巡逻一步 Yield 后续跑」；缺 Effect 写 InvokeAction 的专用失败文案测试 | Showcase Runtime Detail；`action_lib.json`；`graphs.json` BT/HFSM 叶子；合同 L208 vs 资产 L23–L25 |

### 3.5 Minor / 债务

| 严重度 | 项 | 证据 |
|--------|----|------|
| Minor | `GraphActionCatalogLoader` 的 FuncLib 同名检查在 `functions == null` 时跳过（生产 `GameEngine` 有传，脚枪手雷） | Loader ctor L20–L26、L52–L55 |
| Minor | 线性 `graphId` 拒绝、Effect 调 ActionLib 名失败——实现有、**缺**对应 FrontDoor 测试 | Linear L139–L150；表达力测试未覆盖 |
| Minor | 作者写字面 `InvokeAction` 时走「未知 op」，不是 UAT 要求的「Action 不得进入效果事务」语义诊断 | 无 `InvokeAction` 节点；UAT L215–L219 |
| Debt | 合同正文仍写 `InvokeFunc`/`InvokeAction`/`bt.patrolStep`，实现选 `InvokeScript.functionName` + 宿主 `RequireActionId`——应回写合同「等价落地」或改实现，禁止名实双轨 | 合同 L84–L100、L208；状态行已写 InvokeScript |
| Debt | FuncLib Loader 只验 Yield，不验 ApplyEffect/关系/订单等副作用（合同 §5） | Loader L88–L92 |
| Debt | 旧 `GraphCompiler` 图/文档残留（`tag-display-lookup.md`、diagrams specs） | 检索命中 |
| Debt | 删除 `GraphAuthoringFormatPerfCompareTests` 后无替代对照 perf 证据 | commit `76aaecf21` |
| Debt | Champion 火→冰真机录屏（#895/#906 遗留，Xvfb SIGSEGV） | 交接 §5；非本 PR 主责但未消失 |
| Debt | #861 S2–S4：next-chain **源码编译器已删**（本 PR 关闭一大块）；其余「全 Kind 迁 CF / Epic 关单」仍勿宣称已关 | #906 附录；本 PR `76aaecf21` |

### 3.6 声称交付逐项证伪（交接 §2）

| 声称 | 结果 |
|------|------|
| `action_lib.json` + catalog | **成立** |
| `GraphActionCatalog` + Loader | **成立**（空表门禁不成立 → B1） |
| 引擎顺序 graphs → FuncLib → patch → ActionLib | **成立**（`GameEngine.cs` L907–L918） |
| FuncLib purity + Yield 失败关闭 | **成立（有条目时）** |
| ActionLib Script-only + 同名失败关闭 | **成立（生产传 FuncLib 时）** |
| FuncLib 缩为 pure | **成立**（slash/bash/demo 均为 ConstInt 桩） |
| Effect `InvokeScript.functionName` + BranchBool | **成立** |
| Score/Validation/Derived 调 FuncLib、拒 BranchBool | **成立**；Query **不成立**（M1） |
| L2 ScriptKeys + `RequireActionId` + Showcase Bind | **成立** |
| 删除 GraphCompiler/Validator | **成立** |
| 合同「已落地」 | **不成立**（B1 + M1 + M5） |

### 3.7 与 #906 / #909 衔接

| #906 项 | #911 tip 状态 |
|---------|----------------|
| B1 Commit 非原子 | **已关**（#909 cherry-pick）：`CommitNextCastSafeFrame` 收集 rollback，失败回滚（`LiveGasEditPipeline.cs` L176+；测试 `CommitNextCast_PartialFailure_RollsBackAllCandidates`） |
| B2 热注册新 Tag | **已关**：未知 tag → `EngineRestartRequired`，不再 `TagRegistry.Register` |
| M1 impact/hit 串扰 | **已关**（字段分离路径仍在分支） |
| FuncLib 缺文件静默空表 | **未关**，并复制到 ActionLib → 本报告 **B1** |
| 旧 GraphCompiler 债 | **源码已删**（本 PR 关闭） |
| Champion 真机证据 / registry Vignette 等 | **仍开**（本审计不改写为 LSW 专审；记债务） |
| #861 S2–S4 | **部分关**（编译器删除）；勿标 Epic 关单 |

栈风险：#911 叠 #895；#910 合同 docs→main；#912 仅交接。合入顺序须维护者拍板——**不得**在未关 B1 前把合同状态「已落地」合进 main。

### 3.8 GAS composition gate

`artifacts/gas-composition-gate.md` 对「删旧编译器 / Effect 表达力」自审结论 PASS，复用既有 FrontDoor/CF——**未走过场到「新建平行管线」**；但未覆盖 B1 静默空表与 Arena 门槛放宽，不能当作合入通行证。

---

## 4. 场景（业务语言）

1. **配置包漏打了 `action_lib.json`**  
   引擎仍能启动，行为树/关卡要解析动作名时才崩。作者看不到「动作库没装上」的加载期失败——和「缺文件必须立刻失败」的约定不符。

2. **查询图想复用「距离衰减」纯函数**  
   打分/效果阶段可以调，查询阶段前门直接不认——同一套纯函数库对查询作者是断的。

3. **CI 说思考波仍叫「五毫秒」**  
   门槛已放到 20/40ms，测的还不是带动作库叶子的真实路径。性能变差可能被绿灯盖住。

4. **新人打开巡逻/哨兵 Showcase**  
   界面 Detail 在报 ActionLib/Script 术语；叶子图大多是立刻返回的桩，看不到「走一步再想」的跨拍故事。喝水沙盒才真正 Yield。

5. **作者按合同文档写 `InvokeAction` / `bt.patrolStep`**  
   实现对不上文档用词与动作名——不是产品新争论，是名实未回写。

---

## 5. 边界

**覆盖**

- 交接列出的合同、Catalog/Loader、编译器、L2 Showcase、Arena、表达力测试、#906 衔接、#909 是否仍在 tip  
- 合同 §3/§5/§6 与「已落地」声明

**不覆盖 / 不替代**

- 本云重跑 Champion 全路径录屏（已知 Xvfb 债）  
- 替 #615 收完 Save/AI/UI 尾巴（#906 范围；仅核实 #909 阻断修复仍在）  
- 修改生产代码（本交付仅为审计报告）

---

## 6. UAT（合入前必须成立）

```gherkin
Feature: 动作库与纯函数库加载必须看得见
  作为技能与关卡作者
  我希望少了动作库文件时游戏拒绝启动并说明原因
  以便我不会在进关卡后才发现巡逻动作全部失踪

  Scenario: 缺少动作库文件时加载失败
    Given 配置目录声明了 GAS/action_lib.json
    And 实际文件不存在
    When 引擎加载图复用库
    Then 加载必须失败
    And 失败信息必须指出动作库文件缺失或为空

  Scenario: 空动作库表不得当作成功
    Given GAS/action_lib.json 存在但内容是空数组
    When 引擎加载动作库
    Then 加载必须失败
    And 不得留下一个空的动作目录供后续静默使用

Feature: 查询阶段也能复用纯函数
  作为技能作者
  我希望查询图与效果图共用同一套衰减计算
  以便选目标与结算不会各写一套

  Scenario: Query 图调用 FuncLib
    Given FuncLib 中登记了纯函数 damage.falloff
    And 某 Query 图通过 InvokeScript.functionName 调用它
    When 图通过作者前门编译
    Then 编译应成功
    And 运行不得 Yield

Feature: 效果阶段不能拖进可挂起动作
  作为技能作者
  我希望效果结算一次跑完
  以便不会因为误绑巡逻动作而把技能卡在半拍

  Scenario: 效果图不能按动作库语义挂起
    Given 作者在 Effect 图中试图调用仅属于 ActionLib 的名字或挂起动作
    When 图编译或符号修补运行
    Then 必须失败关闭
    And 失败原因应说明动作不得进入效果事务

Feature: 思考波性能门禁说人话且测真路径
  作为维护者
  我希望五毫秒门禁要么守住要么改名并说明测的是什么
  以便放宽门槛不会掩盖真实退化

  Scenario: 门槛与方法名、测量路径一致
    Given Arena 验收测试声明了思考波预算
    When 查看断言与计时范围
    Then 预算数字、方法名与是否包含 ActionLib 叶子执行三者必须一致
    And 不得把计时器外的启动开销当作放宽热路径的理由
```

---

## 附录 A — 交叉审计过程

| 路 | 焦点 | 结论摘要 |
|----|------|----------|
| Explore A | 库边界 | B1 静默空表；名路径闭合；graphId 嵌套洞 M3；bootstrap M4 |
| Explore B | Effect 表达力 | §3.5 PASS；Query M1；命名选择 PASS + 文档债 |
| Explore C | 旧编译器 + L2 + Arena | 编译器 PASS；L2 绑定 PASS；Arena M2；§6 M5 |
| 审计人复读 | B1/M2/Loader/GameEngine/Query/Arena/#909 | 源码确认；无阻断被推翻 |

合并就绪：**DO NOT MERGE**。

---

## 附录 B — 范围快照

- 审计 tip：`20bf1e031`  
- Diff 规模（相对 #895 base）：约 77 files，+2582 / −2776  
- 关键路径：  
  - `assets/Configs/GAS/{func_lib,action_lib}.json`、`config_catalog.json`  
  - `src/Core/GraphRuntime/GraphActionCatalog.cs`  
  - `src/Core/NodeLibraries/GASGraph/Host/Graph*CatalogLoader.cs`  
  - `GraphControlFlowCompiler*.cs`、`GameEngine.cs`  
  - L2 Showcase Runtimes / ScriptKeys / `GraphRegistryScriptResolver`  
  - `src/Tests/GasTests/Graph/Graph*Catalog*Tests.cs`、`GraphEffectAuthoringExpressivenessTests.cs`  
  - `gitbook/architecture/graph-funclib-actionlib-contract.md`

### 合入前最低修复清单（按优先级）

1. **B1** FuncLib/ActionLib：catalog 已声明则缺文件或空表 → 加载失败关闭（修 Loader 或 `MergeArrayByIdFromCatalog` 合同，禁止只靠使用点迟到失败）  
2. **M1** Query 前门白名单纳入 FuncLib 调用（`InvokeScript.functionName`）+ 测试  
3. **M2** 恢复或重设 Arena 预算：测量路径含真实叶子执行，或改名/改注释去掉「FiveMs」谎言；禁止用 bootstrap 解释热路径放宽  
4. **M4** 测试 bootstrap 改走生产 Loader，或增加「核心资产经生产 Loader」守卫  
5. **M3/M5** 收紧 Script `graphId` 绕库或加守卫；补 Effect→ActionLib 失败测试；Showcase/合同命名与 Yield 演示对齐（回写合同或改资产）  
6. 合同状态：阻断未关前改回「实现中/对照中」，禁止「已落地」

---

## 附录 C — 给修复 Agent 的最短提示词

```text
按 docs/audits/pr911_funclib_actionlib_architecture_audit.md 修复 PR #911，禁止重开产品争论。

必须先关阻断 B1：
- GAS/func_lib.json 与 GAS/action_lib.json 在 catalog 已声明时，缺文件或空数组合并结果必须加载失败关闭（对照合同 §5）。
- 补测试：缺文件 / [] → AggregateException 或等价失败；有条目才成功。

然后按报告 Major 顺序：
- M1 Query 白名单允许 InvokeScript.functionName（FuncLib）+ FrontDoor 测试
- M2 Arena 门槛与测量路径/方法名对齐；不得用计时器外 bootstrap 当放宽热路径理由
- M4 bootstrap 走生产 Loader 或加生产 Loader 资产守卫
- M3/M5 边界与 UAT/命名（合同回写或实现对齐，二选一写清）

纪律：NO FALLBACK、SSOT、Data-Driven；先读 gitbook/contributing/ai-assisted-development.md 决策规范。
不要顺手改无关 #615 尾巴；Champion 录屏债可单列。
完成后更新审计报告勾选或另开修复 PR，并把合同「已落地」与真实状态对齐。
```
