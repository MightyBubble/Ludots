# 审计需求：PR #911 FuncLib / ActionLib 拆分与 Effect 表达力

**给审计 / 接手 Agent。**  
这是一次**独立架构审计请求**，对象是 PR #911（叠在 #895 之上）。不要只扫 diff 文件名；必须对照合同文档做「产品边界是否闭合」审查。

---

## 0. 一句话主线

在「一种作者边模型 + 一台 VM」已成立的前提下，把原先混在一起的 **FuncLib** 拆成：

1. **FuncLib** = 纯函数（无 Yield，供 Effect/Score/… 调用）  
2. **ActionLib** = 可挂起动作（可 Yield，只给 L2 / Script 切片宿主）  
并补齐 **Effect 阶段内分支 + 调纯函数**，同时**删掉** next-chain 旧编译器。

| PR / 文档 | 状态 | 角色 |
|-----------|------|------|
| #895 | OPEN | 底座：#861 零旁路/FuncLib 初版 + #615 LSW 热应用 |
| #906 | OPEN（审计报告） | 对 #895 的交叉审计（DO NOT MERGE 结论） |
| #909 | OPEN | 按 #906 修 #895 阻断项（已 cherry-pick 进 #911） |
| #910 | OPEN（docs→main） | FuncLib/ActionLib **合同草案**（产品对齐） |
| **#911** | **OPEN（本审计对象）** | 合同**实现落地** |

当前被审分支：`cursor/funclib-actionlib-impl-45dc`  
PR：https://github.com/MightyBubble/Ludots/pull/911  
Base：`cursor/bc-0bbd794f-c30a-4fd5-8b62-2ece111a5e55-79e6`（#895 head）  
文档入库时 tip：`20bf1e031`（后续以 PR 最新 tip 为准）。

---

## 1. 产品共识（审计前先对齐，勿再争）

以下已与产品侧对齐，审计以「是否忠实落地」为主，不是重新开产品讨论：

1. **Duration / Period** 属于效果生命周期壳；OnPeriod 再跑阶段图。Effect 图内 **不用 Yield 冒充时间轴**。  
2. **Score / Validation /（多数）Query / Derived** 可以是纯/准纯。  
3. **Effect** 事务中途禁止挂起；但必须至少能：**分支**，以及/或者 **调用 FuncLib**。  
4. **FuncLib ≠ ActionLib**：前者纯、后者可 Yield/可副作用；同名禁止跨库。  
5. 仍 **不做** 编译期文本 Macro；ActionLib 是登记的可调用图，不是粘贴展开。

合同 SSOT（实现已标「已落地」，审计需核实是否名实相符）：

- `gitbook/architecture/graph-funclib-actionlib-contract.md`
- `gitbook/architecture/graph-layering-flow-and-behavior.md`

UAT 场景语言以合同 §4 / §6 Cucumber 为准（玩家/作者视角，不要改成架构八股）。

---

## 2. 声称已交付（请逐项证伪）

### 2.1 Catalog / 加载

| 声称 | 关键路径 |
|------|----------|
| `GAS/action_lib.json` 进 catalog | `assets/Configs/GAS/action_lib.json`、`config_catalog.json` |
| `GraphActionCatalog` + Loader | `src/Core/GraphRuntime/GraphActionCatalog.cs`、`…/Host/GraphActionCatalogLoader.cs` |
| 引擎顺序：graphs → FuncLib → patch → ActionLib | `GameEngine.cs`（加载段） |
| FuncLib 强制 `purity=pure`、程序含 Yield → 失败关闭 | `GraphFunctionCatalogLoader.cs` |
| ActionLib 仅 Script；与 FuncLib 同名 → 失败关闭 | `GraphActionCatalogLoader.cs` |
| FuncLib 资产缩为 pure（slash/bash/demo） | `assets/Configs/GAS/func_lib.json` |
| 服务键 | `CoreServiceKeys.GraphActionCatalog` |

### 2.2 Effect / 线性 Kind 表达力

| 声称 | 关键路径 |
|------|----------|
| 线性 Kind 可 `InvokeScript.functionName`（FuncLib） | `GraphControlFlowCompiler.Linear.cs` |
| Effect 允许 `BranchBool`；Score/Validation/Derived 拒绝 | `GraphControlFlowCompiler.cs`（`IsBranchBoolAuthorable`） |
| Effect 仍禁 Wait/While/Until/Yield | 既有 Kind 政策 + 测试 |
| 守卫测试 | `GraphEffectAuthoringExpressivenessTests.cs` |

### 2.3 L2 / Showcase 零旁路（ActionLib）

| 声称 | 关键路径 |
|------|----------|
| ScriptKeys 改为 ActionLib 名 | `BehaviorTreeScriptKeys` / `HfsmScriptKeys` / `LevelScriptKeys` |
| 解析走 `RequireActionId` | `GraphRegistryScriptResolver.cs` |
| Showcase Bind(Registry, ActionCatalog) | BT Arena / HFSM / Level / ScriptFlow / Integration |
| 测试 bootstrap 加载 ActionLib | `GraphRegistryTestBootstrap.cs`、AI runtime tests |

### 2.4 旧编译器退役

| 声称 | 关键路径 |
|------|----------|
| 删除 next-chain `GraphCompiler` / `GraphValidator` | 仓库内 `src/` 应无残留引用 |
| 测试迁 ControlFlow / FrontDoor | GasTests Graph/*；对照 perf 测试已删 |
| 文档声明 CF 为唯一编译器 | `graph-layering-flow-and-behavior.md` |

### 2.5 与 #895 / 热应用的关系

- #911 **包含** #909 审计修复 cherry-pick（原子 Commit、未知 Tag 失败关闭、impact/hit 分离等）。  
- LSW Champion 火→冰热应用路径 **未重做产品故事**；Ability 仍走 FuncLib pure（`ability.slash` / `ability.bash`）。  
- 审计 #911 时若发现热应用回归，记入报告，但主责仍是复用库边界——勿把整份报告写成只审 LSW。

---

## 3. 审计必问（阻断级优先）

### A. 库边界是否真闭合

1. 是否仍存在「名义 FuncLib、实际可 Yield」的登记或调用路径？  
2. Effect（或其它线性 Kind）是否能间接 Invoke **ActionLib** / 含 Yield 的 Script？  
3. L2 叶子是否仍有 `RequireId("Graph.…")` 旁路、本地程序字典、或绕过 ActionCatalog？  
4. 缺 `action_lib.json` / 空表 / 同名冲突是否 **失败关闭**（无静默空 catalog）？

### B. 作者表达力是否与合同一致

5. Effect `BranchBool` 是否只是糖→Jump，且未偷偷放开 Wait/While/Yield？  
6. 线性 `InvokeScript` 是否只允许 `functionName`（FuncLib），`graphId` 直绑是否被正确拒绝？  
7. Score/Validation/Derived 调 FuncLib 是否可用？BranchBool 是否按合同拒绝？

### C. 旧世界是否残留

8. `src/` 是否仍有 next-chain `GraphCompiler` / `nodes[].next` 生产路径？  
9. Editor Bridge / 工具链是否仍引导作者写旧格式？  
10. 文档「已落地」是否与代码一致，有无「草案/对照测试保留」谎言？

### D. Showcase / 验收是否说人话

11. 五个图行为 Showcase 是否从玩家视角仍成立（巡逻/哨兵/关卡/喝水/整合），Detail 是否只堆术语？  
12. 合同 §6 Cucumber 场景是否有自动化或手工 UAT 映射？缺则记 Major/债务，勿假装已验收。  
13. 热路径预算（思考波 ≤5ms 等）在 ActionLib 解析后是否被「放宽门槛」掩盖真实退化？（见 arena CI gate 调整）

### E. 栈与合入风险

14. #911 叠在 #895 上：合入顺序、与 #909/#910 是否重复/冲突？  
15. 是否把 #861 S2–S4 未竟项误标为已关？

---

## 4. 建议审计产出

请另开审计报告 PR（建议文件名）：

`docs/audits/pr911_funclib_actionlib_architecture_audit.md`

报告至少包含：

1. **Verdict**：MERGE / MERGE WITH FIXES / DO NOT MERGE  
2. **阻断 / Major / Minor / 债务** 表（路径 + 证据）  
3. 对合同 §3 / §5 / §6 的逐条符合性  
4. 与 #906（#895 审计）结论的衔接：哪些已被 #909+#911 关闭，哪些仍开着  
5. 给修复 Agent 的最短提示词

更新 `docs/audits/README.md` 目录链接。

交叉审计可用多个子 Agent，但**最终只留一份 SSOT 报告**。

---

## 5. 已知已知（勿当新发现重复挖，但要核实是否仍在）

| 项 | 说明 |
|----|------|
| Champion Xvfb SIGSEGV | #895 遗留：真机火→冰录屏在本云不稳定；属验收证据债，不是本 PR 主交付 |
| Arena 性能门槛放宽 | `GraphBehaviorArenaAcceptanceTests` 曾因 ActionLib bootstrap 放宽 avg/p95；审计应判断是否掩盖退化 |
| #910 与 #911 | #910 是合同 docs→main；#911 实现叠 #895。合入策略需产品/维护者拍板 |
| InvokeAction 独立 opcode | 合同实现切片建议过「InvokeFunc/InvokeAction」；本 PR **复用** `InvokeScript.functionName`=FuncLib。审计判断「两套名字并存」风险是否成立 |

---

## 6. 必读文件清单（最短）

**合同**

- `gitbook/architecture/graph-funclib-actionlib-contract.md`
- `gitbook/architecture/graph-layering-flow-and-behavior.md`

**实现**

- `assets/Configs/GAS/{func_lib,action_lib}.json`
- `GraphActionCatalog*.cs` / `GraphFunctionCatalogLoader.cs`
- `GraphControlFlowCompiler.cs` + `.Linear.cs`
- `GraphRegistryScriptResolver.cs` + L2 ScriptKeys
- Showcase：`CapabilityStandard*BehaviorTree*`, `*Hfsm*`, `*Level*`, `*ScriptFlow*`, `*GraphBehaviorIntegration*`
- `GameEngine.cs`（catalog 加载顺序）

**测试**

- `GraphEffectAuthoringExpressivenessTests.cs`
- `GraphActionCatalogLoaderTests.cs`
- `GraphFunctionCatalogLoaderTests.cs`
- AI：`BehaviorTreeRuntimeTests` / `FsmRuntimeTests` / `LevelDirectorRuntimeTests`
- Showcase acceptance：`GraphBehavior*ShowcaseAcceptance*`

**前置审计**

- `docs/audits/pr895_graph_infra_and_lsw_architecture_audit.md`（#906）
- `docs/audits/pr895_graph_infra_and_lsw_audit_handoff.md`

**纪律入口**

- `gitbook/contributing/ai-assisted-development.md`（任务执行决策规范）
- `artifacts/gas-composition-gate.md`（本实现自审；审计可质疑是否走过场）

---

## 7. 给审计 Agent 的最短提示词（可直接贴）

```text
审计 PR #911（https://github.com/MightyBubble/Ludots/pull/911），
分支 tip 以 PR 最新为准；base 为 #895 head。

先读：
- docs/audits/pr911_funclib_actionlib_audit_handoff.md（本交接）
- gitbook/architecture/graph-funclib-actionlib-contract.md
- docs/audits/pr895_graph_infra_and_lsw_architecture_audit.md（衔接）

产品共识已对齐：FuncLib=纯；ActionLib=可挂起；Effect 可 BranchBool+调 FuncLib；
Duration/Period 不走 Yield；禁止 Effect 调 Action；同名跨库失败关闭；旧 GraphCompiler 应已死。

请交叉审计并只产出一份报告：
docs/audits/pr911_funclib_actionlib_architecture_audit.md
Verdict 必须明确；阻断要有路径与复现/证据；对照合同 §5 边界与 §6 UAT。
可用子 Agent，但禁止多份平行结论。
纪律：NO FALLBACK、SSOT、Data-Driven、只改自己工作区；先读 ai-assisted-development 决策规范。
不要重开产品争论；发现合同与实现不一致时记缺陷，或建议回写合同——二者择一写清。
```

---

## 8. 本需求文档的范围

- **包含**：发起审计所需上下文、检查清单、产出格式。  
- **不包含**：审计结论本身（留给报告 PR）。  
- **禁止**：借审计需求 PR 夹带实现修复。
