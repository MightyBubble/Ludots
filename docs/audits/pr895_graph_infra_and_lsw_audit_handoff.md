# 审计交接：整段会话 Graph 基建 + Epic #615（勿只看热应用尾巴）

**给审计 / 接手 Agent。**  
上一版交接只写了 LSW 热应用，**漏掉了会话前半段真正的主体：图基建分层、Script VM、L2 调度、Showcase、作者 SSOT/FuncLib**。本文件按**会话时间线从头**重写。

---

## 0. 会话主线（一句话）

从 PR #736 的「另起一套 VM」审计结论出发 → **并入现有 GASGraph** → 做出 **L0/L1/L2 分层（对标 FlowCanvas / NodeCanvas）** → **万人级 BT / HFSM / 关卡蓝图 / 技能图 Showcase** → 清掉旁路收成 **真正数据引脚 + Func Lib** → 再在此之上做 **Epic #615 热编辑热应用**。

| PR / Issue | 状态 | 会话中的角色 |
|------------|------|----------------|
| #736 | 审计结论：不建议直接合 | 起点：Yield/ExecuteSlice/CallStack/SourceMap 能力要迁入 GASGraph，不是平行 VM |
| **#848** | **已合 main** | Script + HFSM/BT/Level + 分离 Showcase 的主体交付 |
| #859 | 已合（#848 后） | Query 也走 controlEdges/valueEdges（补 #848 遗漏） |
| **#861** | 进行中（在 #895 分支上落地） | 作者 SSOT + Func Lib + 零旁路（L2/沙盒不得私藏程序宇宙） |
| #860 | 分离 | 将来只换执行后端，不得再引入第二套作者格式 |
| **#615** | 本分支收口中 | Real-time Skill Workbench；用户要求**子单全做完**，不是只做热应用核心 |
| **#895** | OPEN | 当前工作 PR：#861 尾巴 + #615 全量 + Champion 火→冰真机证据 |

当前分支：`cursor/bc-0bbd794f-c30a-4fd5-8b62-2ece111a5e55-79e6`  
PR：https://github.com/MightyBubble/Ludots/pull/895

文档入库时分支 tip：`fdd24e21e`（后续以 PR 最新 tip 为准）。

---

## 1. 阶段 A — #736 后续：并入 GASGraph，不要第二套 VM

**用户意图：** 审计认为 #736 不宜直接合；要把创新点接到 **main 已有 GASGraph**，并为未来 BT/FSM/关卡蓝图留分层。

**参考模型（业务对齐）：**

- **细粒度脚本** ≈ Paradox Notion FlowCanvas → 本仓库 **L1 Script / Effect / Score / Query…**
- **粗粒度行为调度** ≈ NodeCanvas → 本仓库 **L2 BehaviorTree / HFSM / LevelTrigger**（不是再塞一套 GraphKind）

**分层合同（已写进文档与代码）：**

```text
L0 引擎：GraphInstruction VM + GraphProgramRegistry + GasGraphOpHandlerTable
         Execute（跑到停）/ ExecuteSlice（可 Yield 续跑）+ SourceMap
L1 方言：Script（新）, Effect, Score, Query, Validation, Derived
L2 调度：BehaviorTree / HFSM / LevelDirector —— 自己的拓扑，叶子/回调只 Invoke L1 GraphId
```

**L0 关键能力（已在 main，#848）：**

- `GraphKind.Script`
- Op：`Call` / `Return` / `Yield` / `HaltReturnInt` / `InvokeScript` / `MoveInt`
- `GraphExecutionCursor` + `GraphSliceResult`
- `GraphInstructionSourceMap`
- `GraphControlFlowDocument` + `GraphControlFlowCompiler`（作者：controlEdges + valueEdges）
- CallStack：**禁止堆兜底**；调用方自备栈；`InvokeScript` 子状态自带 `stackalloc` 栈
- `GraphCompiler` **硬拒** Kind=Script（及随后 Query）走旧 Next 链

**性能合同（用户压过预算）：**

- 渲染目标 **60 FPS**
- AI 思考可 0.2s 一波，但 **整波 AI/图合计压到 ≤5ms**（用户否决 25ms）
- 热路径 **0-alloc**；禁止「图底层分帧休眠」当解药
- 万人级压力：BT/HFSM SoA（`BehaviorTreeWorld` / `HfsmWorld`）+ 基准矩阵 `docs/benchmarks/graph-behavior-pressure/`

---

## 2. 阶段 B — L2 行为调度（BT / HFSM / Level）+ Ability

**BT：**

- SoA 世界 + 叶子通过 GraphId 跑 Script（后经 #861 完全 Registry）
- 传感器喂入 `I[0]`；Host 侧不得私藏平行程序表
- Showcase：巡逻 → 发现敌人 → 追击/攻击（可读小剧场，前景约 12 人 + 可选万人灰点压力）

**HFSM（用户明确要层级状态机，灵感来自 Animator，但禁止玩法/表现搅在一起）：**

- 转移条件挂在 **transition** 上（Condition GraphId）
- 状态生命周期：`OnEnter` / `OnTick` / `OnExit` 可配 Script
- `GraphProgramHfsmHost`；OnTick 有图却无 host → **硬失败**（禁止静默跳过）
- Showcase：哨兵巡视 / 警戒 / 交战

**Level Blueprint：**

- `LevelActionKind.RunScript` + `GraphProgramLevelHost`
- Showcase：闸门 / 试炼触发链

**GAS Ability / Effect Graph：**

- 技能图走 FuncLib / Registry，不硬编码程序块
- Showcase：能力图沙盒（与 L2 剧本分离；整合演示单独 Mod）

**原子 L1 Showcase：**

- `capability_standard_script_flow_sandbox`（「喝水直到满」——证明 Script Yield/续跑）

**整合演示：**

- `capability_standard_graph_behavior_integration`（单独，不大杂烩）

以上主体在 **PR #848 已合 main**。用户曾要求：录屏加长（约 20–28s）、从抽象螺旋点改成可读 AI 小剧场、截图/录屏齐备。

---

## 3. 阶段 C — 「老留尾巴」：零旁路 + 真 Func Lib（#861）

用户多次指出旁路：

- BT 叶子曾用 C# host / 本地字典，而不是 Script 图绑定
- Level action 缺 Script 口（后已补）
- FuncLib 未真正进加载/编译链路

**#861 在本分支落地（相对 main 仍可见的图基建 diff）：**

| 交付 | 位置 |
|------|------|
| `func_lib.json` | `assets/Configs/GAS/func_lib.json` + catalog |
| `GraphFunctionCatalogLoader` | 加载并校验 GraphKey ↔ Registry |
| `PatchFuncLib` | `InvokeScript` 按函数名解析到 GraphId |
| `GraphProgramAuthoringFrontDoor` | 唯一编译前门；禁 `nodes[].next` |
| 删除 `BehaviorTreePatrolScripts.cs` | 禁本地程序宇宙 |
| `GraphRegistryScriptResolver` | BT 只认 Registry |
| HFSM / Level / 各 Showcase Runtime | 零旁路改造 |
| `GraphAuthoringSsotGuardTests` | 架构守卫 |
| 文档 | `gitbook/architecture/graph-layering-flow-and-behavior.md` |

相关 commits（本分支）：

```
456343c1a feat(graph): true Func Lib + authoring SSOT lock for #861
6ada731ae feat(graph): zero-bypass Registry/FuncLib for all graph showcases
c3649a919 fix(graph): close remaining zero-bypass tails
```

**注意：** #861 Epic 全文还规划 Effect/Score/Validation/Derived **全部**迁到作者 SSOT、废除 next-chain 真相等 S2–S4；本会话优先做了 **FuncLib + L2/Showcase 零旁路 + FrontDoor 锁**，不是宣称 #861 每一个子阶段都已关单——审计时对照 issue #861 正文核对剩余 S2–S4。

---

## 4. 阶段 D — Epic #615 Live Skill Workbench（用户纠正：子单全做）

用户先要求：热重载之前先把数据引脚/FuncLib 基建收好 → 同意后开工。  
一度只交了热应用核心，用户明确：**「又留尾巴」——最初要求是 #615 全部子单，不是只做核心。**

| 切片 | 内容 | 本分支状态 |
|------|------|------------|
| #616/#626 | 架构合同 | 已 merge 进分支 |
| #617/#637 | Debug patch + session | 已 merge |
| #655/#656 | Workbench UI 基础 | 已 merge |
| #618/#619/#622 | Stage/Classify/Commit + Tag/Attr 热应用 | 已实现 |
| #620 | Immediate 属性命令 | `LiveAttributeCommandExecutor` |
| #621 | 效果链 Tracer（有界 + Dropped） | `LiveEffectChainTracer` |
| #623 | AI draft + binder | `LiveAiSkillDraft` / binder |
| #624 | 接受草稿落盘 Mod | `LiveEditModSaveService` |
| #625 | UAT（Cucumber） | `gitbook/acceptance/live-skill-workbench-uat.md` |
| Showcase | 真机证明编辑器热应用 | 见阶段 E |

**热应用合同：**

- Classify：`Immediate` / `NextCastLiveApply` / `MapReloadRequired` / `EngineRestartRequired`
- Registry 热替换：`ReplaceProgram`、Effect 数值/弹道引用/GrantedTag、Tag 规则体、Attr 约束（**禁止静默、禁止新身份热加**）

---

## 5. 阶段 E — 验收证据反复打回（当前缺口）

用户连续否决不合格证据：

1. 纯 Workbench UI 截图 →「不可视化」
2. 圆点「玩家输入输出」vignette →「测的是编辑器热应用，不是玩家 I/O」
3. Skia 证据板 / 假 Demo →「不是真 Showcase Mod」
4. 最终口径：**真机 Showcase + 生产 `LiveGasEditPipeline` + 禁止 hardcode/造假**

**最终落点代码：** Champion 风格「发火球 → 热改 → 再发变冰球」  
`CapabilityStandardLiveSkillWorkbenchShowcaseMod` + `LswChampionHotApplyDemoSystem`  
Preset：`capability_standard_live_skill_workbench_raylib`

**未完成：** 本云 Xvfb + llvmpipe 上 Champion 真弹道路径常在 instancing shader 后 **SIGSEGV**，未能稳定交付完整真机火→冰录屏。  
（轻量 DebugDraw 路径曾可抓；Champion 路径不稳定。Linux `libGL` 解析修复已落地，仍不足以保证 Champion 全路径。）

---

## 6. 审计时必看的「图基建」文件（勿只扫 LSW 目录）

### 已在 main（#848 主体，会话前半成果）

- `src/Core/GraphRuntime/`：`GraphKind.Script`, Cursor, SourceMap, ControlFlowDocument, FunctionCatalog…
- `src/Core/NodeLibraries/GASGraph/`：Ops、`ExecuteSlice`、ControlFlowCompiler、KindPolicy、AuthoringFrontDoor…
- `src/Core/Gameplay/AI/BehaviorTree/*`、`AI/Fsm/*`、`Level/*`
- Showcase Mods：ScriptFlow / BT Arena / HFSM Sentry / Level Trial / Ability Sandbox / Integration
- `gitbook/architecture/graph-layering-flow-and-behavior.md`
- 压力矩阵：`docs/benchmarks/graph-behavior-pressure/`

### 本分支相对 main 仍突出的图基建（#861 收尾）

- `assets/Configs/GAS/func_lib.json`（新）
- `GraphFunctionCatalogLoader`、`GraphInstructionFlags`、`PatchFuncLib`
- `GraphProgramRegistry.ReplaceProgram`（亦服务热应用）
- BT/HFSM/Level/Showcase 去旁路；删 `BehaviorTreePatrolScripts.cs`
- `GraphAuthoringSsotGuardTests`

### 本分支 LSW / #615

- `src/Core/Gameplay/GAS/LiveSkillWorkbench/*`
- Effect/Tag/Attr Registry 热替换 API
- LSW Showcase Mod + Champion fire→ice demo
- GasTests：`LiveGasEditPipelineTests`、`LswFireToIceHotApplyTests`、Epic/Showcase acceptance

---

## 7. 用户反复强调的纪律（审计对照）

1. **NO FALLBACK / 禁止静默失败**（CallStack 堆兜底已删；HFSM OnTick 无 host 硬失败）
2. **SSOT / DRY**：一种作者边模型；L2 只引用 GraphId
3. **Data-Driven，NO HARDCODE**：图进 `graphs.json` / FuncLib，不进 C# 字典宇宙
4. **分层职责**：L2 不重新发明 L1 VM；玩法调度 ≠ 表现 Animator
5. **ECS**：SoA、Chunk、0-alloc 热路径；禁热路径结构变更
6. **Showcase = 新玩家能看懂的小剧场**，不是技术流水账；证据要真机生产链路
7. **不要留尾巴**：旁路、半吊子 FuncLib、#615 只做核心都不合格

---

## 8. 给接手 Agent 的最短提示词（可直接贴）

```text
会话全文从 #736 并入 GASGraph 开始，不是只做热重载。

已合 main（#848）：L0 ExecuteSlice/Call/Return/Yield/InvokeScript + SourceMap；
L1 Script；L2 BT/HFSM/Level；分离 Showcase + ScriptFlow 原子沙盒；性能 ≤5ms/思考波。

本分支 PR #895 还要审计：
1) #861：func_lib 真加载、InvokeScript 按名解析、L2/Showcase 零旁路、AuthoringFrontDoor 锁；
   对照 #861 正文核对 S2–S4（全 Kind 迁 CF、废 next-chain）是否仍有债。
2) #615 全子单：LiveGasEditPipeline + Immediate 属性 + Tracer + AI draft + Save + UAT + Workbench 接线。
3) 验收：preset capability_standard_live_skill_workbench_raylib，
   真机 Champion 发火球→LiveGasEditPipeline 热改→再发冰球；禁止假伤害/证据板冒充。
   已知本云 Xvfb 上 Champion 路径易 SIGSEGV——优先修稳定录屏，不要再交 UI-only。

纪律：NO FALLBACK、SSOT、Data-Driven、说人话验收、只改自己工作区。
先读 gitbook/contributing/ai-assisted-development.md 任务执行决策规范，再动代码。
```

---

## 9. 上一版交接的错误

上一版 `AUDIT-HANDOFF-PR895.md` **从 #861/LSW 起笔**，把会话前半（#736 审计、分层、Script VM、L2、万人 Showcase、可读剧场返工、#848）缩成一句「主题演进 A」，等于没提图基建主体。  
**以本文件为准。**
