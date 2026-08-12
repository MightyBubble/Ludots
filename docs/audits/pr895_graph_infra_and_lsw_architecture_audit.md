# PR #895 架构审计：图基建 + Epic #615（交叉审计）

**审计对象：** PR #895（`cursor/bc-0bbd794f-c30a-4fd5-8b62-2ece111a5e55-79e6`）  
**审计 tip：** `8c9dba010`（`fix(lsw): bind local viewer and Health knowledge so WorldHud can project`）  
**基线：** `main` @ `f394f8742`  
**交接 SSOT：** [`pr895_graph_infra_and_lsw_audit_handoff.md`](pr895_graph_infra_and_lsw_audit_handoff.md)  
**合同对照：** `gitbook/architecture/graph-layering-flow-and-behavior.md`、`gitbook/architecture/live-skill-workbench.md`、`gitbook/contributing/ai-assisted-development.md`

---

## 1. 概述

按交接文档完整会话弧（#736 → #848 → #861 → #615）做严肃审计，**不是只扫 LSW 热应用尾巴**。  
三路独立探索 + 一路对抗复核交叉验证关键缺陷。

**合并结论：禁止合入（DO NOT MERGE）。**

| 主线 | 结论 |
|------|------|
| #848 已合 main 的 L0/L1/L2 主体 | 本审计不重复否决；生产加载仍走 FrontDoor |
| #861 FuncLib + 零旁路 + FrontDoor 锁 | **可部分通过**；S2–S4 债未清；FuncLib 缺文件可静默空表 |
| #615 全子单 | **未达标**；Commit 非原子、可热注册新 Tag、落盘/UI/AI 尾巴未收 |
| Champion 火→冰真机证据 | **不可接受**；吞异常、视觉旁路、registry 仍挂 Vignette、无 artifact |

---

## 2. 结构

```text
1 概述 / 合并结论
2 结构（本页）
3 详情：方法、分阶段结论、缺陷清单
4 场景：玩家/作者会撞到什么
5 边界：本审计覆盖与不覆盖
6 UAT：合入前必须成立的验收（Cucumber）
附录 A 交叉审计过程
附录 B tip / 范围文件
```

---

## 3. 详情

### 3.1 审计方法

1. 读交接全文与任务执行决策规范。  
2. 三路只读审计：  
   - A：#861 图基建（FuncLib / FrontDoor / L2 零旁路 / S2–S4）  
   - B：#615 LiveGasEditPipeline 与全子单  
   - C：Showcase / 证据链  
3. 第四路对抗复核：对 Blocker/Major **逐条验真或推翻**。  
4. 审计人再对 Blocker 源码逐段复读（`LiveGasEditPipeline` Commit、`ClassifyGrantedTag`、Effect 热替换、Champion Demo、Save/Dataplane）。

### 3.2 阶段 A/B（#848 已合 main）— 基线确认

生产图加载：`GameEngine` → `GraphProgramConfigLoader` → `GraphProgramAuthoringFrontDoor.CompileJsonObject`（`GraphProgramConfigLoader.cs` L66–L68）。  
FrontDoor 对正式 Kind **硬拒** `nodes[].next`（`GraphProgramAuthoringFrontDoor.cs` L92–L106）。  
L2 BT/HFSM/Level 缺 host / 缺 Registry 程序时硬失败（非静默）。  
`BehaviorTreePatrolScripts.cs` 已删除。  

**结论：** 会话前半主体在 main 上成立；本 PR 不因 #848 被否。

### 3.3 阶段 C（#861）— PARTIAL

#### 通过项

| 项 | 证据 |
|----|------|
| `func_lib.json` 入 catalog | `assets/Configs/config_catalog.json` 含 `GAS/func_lib.json` |
| 引擎顺序：登记图 → 加载 FuncLib → Patch | `GameEngine.cs` ~L907–L911 |
| `InvokeScript.functionName` → `FuncLibName` 旗标 → Patch 成 GraphId | `GraphControlFlowCompiler` / `GraphProgramSymbolPatcher` |
| L2 Showcase 绑 Registry/Catalog，无私藏程序宇宙 | BT/HFSM/Ability sandbox Runtime |
| 资产守卫禁 `nodes[].next` | `GraphAuthoringSsotGuardTests` |

#### 缺陷

| 严重度 | 缺陷 | 证据 |
|--------|------|------|
| Major | catalog 缺 `func_lib.json` 时 **静默空表返回**（文件已在 catalog 声明，不应 optional） | `GraphFunctionCatalogLoader.cs` L36–L42 |
| Debt | 旧 `GraphCompiler` next-chain 源码仍在；线性 CF 操作白名单窄于旧编译器 | `GraphCompiler.cs`；`GraphControlFlowCompiler.Linear.cs` |
| Debt | #861 S2–S4（全 Kind 迁 CF、废除 next-chain 真相等）**未关单**——交接已声明；资产侧已无 `"next"`，源码债仍在 | 交接 §3；`GraphConfig.Next` |

**#861 本切片（FuncLib + 零旁路 + FrontDoor 锁）接近完成，但不能宣称 Epic 关单。**

### 3.4 阶段 D（#615）— FAIL

对照合同：`gitbook/architecture/live-skill-workbench.md` §3.3–3.4。

#### Blocker（合同级）

**B1 — Commit 非原子 / 可半提交**

合同：

> 禁止半提交：任一候选失败，整次 commit 回滚到旧 snapshot。  
> （`live-skill-workbench.md` L134）

实现：`CommitNextCastSafeFrame` 按候选依次 `Replace*`，失败只记诊断；最后 `Clear` staging；`diagnostics.Count > 0` 时返回 `succeeded=false` 且 `applied` 可为正数——**已成功的替换不回滚**。  
证据：`LiveGasEditPipeline.cs` L194–L349。

对抗复核：**VERIFIED**。

**B2 — 可热注册新 Tag 身份**

合同：

> Tag 规则热应用禁止 Register 新 tag 名；身份变更 → `EngineRestartRequired`。  
> （`live-skill-workbench.md` L144、L147）

实现：`ClassifyGrantedTag` 在 `!TagRegistry.IsFrozen` 时 `TagRegistry.Register(tagName)`。  
证据：`LiveGasEditPipeline.cs` L953–L959。

对抗复核：**VERIFIED**。

#### Major

| ID | 缺陷 | 证据 |
|----|------|------|
| M1 | `projectile.impactEffect` 与 `projectile.hitEffect` 热替换**同时写两个字段**（字段串扰） | `EffectTemplateRegistry.cs` L562–L568 |
| M2 | `EffectTemplateRef` Classify 不校验合法 `fieldPath`，坏路径先标 `NextCastLiveApply`，Commit 才失败 → 放大 B1 半提交 | `LiveGasEditPipeline.cs` L908–L919 |
| M3 | 生产 `AiSkillDraftGenerator` 注册为硬编码 `DeterministicFakeAiSkillDraftGenerator`（FrostNova 假草稿） | `GameEngine.cs` L1441；`LiveAiSkillDraft.cs` L40–L104 |
| M4 | #624 落盘：`BindEpicServices` **未注入** `saveModRoot` → UI 永远 “Save service or Mod root is not configured”；`EffectTemplateRef`/`EffectGrantedTag` 无 save mapping；数值写入旁路文件 `lsw_accepted_patches.json`（非 effects.json SSOT） | `LiveSkillWorkbenchModEntry.cs` L63；`LiveSkillWorkbenchRuntime.cs` L266–L273；`LiveEditModSaveService.cs` L114–L141、L208–L213 |
| M5 | Dataplane 只注册 stage/discard/select/precheck/apply；#620–#624 命令 ID/Handler 存在但未 `router.Register` → UI 不可达 | `LiveSkillWorkbenchDataPlaneInstaller.cs` L49–L53 |
| M6 | Pipeline 实例级 staging，无 session revision / 所有权；`Classify` 清空重填 | `LiveGasEditPipeline.cs` L21–L67 |
| M7 | Tracer 未绑定为 UI 刷新时静默 return | `LiveSkillWorkbenchRuntime.cs` L88–L95 |

#### 切片计分

| 切片 | 状态 | 说明 |
|------|------|------|
| #616/#626 架构合同 | PASS | 文档齐全，且**实现违反合同**（见 B1/B2） |
| #617/#637 Patch/Session | PASS | 结构化 patch、不可变 Operations |
| #655/#656 UI/Dataplane | PARTIAL | 基础五命令通；Epic 尾命令未挂 |
| #618/#619/#622 Pipeline | **FAIL** | B1/B2/M1/M2 |
| #620 Immediate Attr | PARTIAL | Executor 存在；UI 未挂 |
| #621 Tracer | PARTIAL | 有界 + Dropped；UI 刷新未挂 |
| #623 AI draft | **FAIL** | 生产假生成器硬编码 |
| #624 Save to Mod | **FAIL** | 无 root、映射不全、数值非 SSOT |
| #625 UAT | PARTIAL | Cucumber 文档在；真机证据不在 |

**「#615 子单全做完」不成立。**

### 3.5 阶段 E（Showcase / 证据）— NOT ACCEPTABLE

| 项 | 结论 | 证据 |
|----|------|------|
| Champion 系统存在且调用生产 Pipeline | PASS（源码层） | `LswChampionHotApplyDemoSystem` Stage/Classify/Commit |
| 失败纪律 | **FAIL** | `catch (Exception)` 写 HUD 后继续画（L154–L163） |
| 弹道颜色证明 | **FAIL** | `_hotApplied \|\|` 即可画冰色，可不看真实 PresentationEffectTemplateId（L652–L654） |
| registry 验收入口 | **FAIL** | `acceptanceTest`: `LiveSkillWorkbenchVignetteShowcaseAcceptanceTests`；summary 仍写 Vignette 叙事（`showcase.registry.json` L3005–L3026） |
| artifact | **FAIL** | `artifacts/evidence/capability_standard_live_skill_workbench` **不存在** |
| tools/lsw-*-evidence | **FAIL（作伪证据源）** | Skia 板 / 合成 registry，非 Raylib 生产录屏 |
| 验收测试 | **FAIL** | 合成 Registry / headless，不启动 Champion 地图断言二发弹道+寒冰状态 |
| Xvfb Champion SIGSEGV | 已知缺口（交接 §5） | Immediate 渲染切换可规避 instancing，**不能**当作已证明 Champion 全路径 |

### 3.6 纪律对照（用户硬规则）

| 纪律 | 审计结果 |
|------|----------|
| NO FALLBACK / 禁止静默失败 | **违规**：B1 半提交；FuncLib 缺文件空表；Champion 吞异常；Tracer null no-op；Mod 缺 Pipeline 只打 log |
| SSOT / DRY | **违规**：数值落盘旁路文件；registry 验收仍挂 Vignette；AI 假生成器当生产服务 |
| Data-Driven NO HARDCODE | **违规**：AI FrostNova 常量图；Champion 硬编码 effect/HP 分母 200；弹道色用 flag |
| 分层职责 | #861 L2→L1 大体正确；LSW Save/UI 未接到完整命令面 |
| Showcase = 可读小剧场 + 真机生产链路 | **未交付**完整火→冰真机证据 |

---

## 4. 场景（业务语言）

1. **作者在工作台改弹道命中效果再点应用**  
   若同批还有别的候选失败，可能出现：一部分改动已经进了运行时，界面却报失败——下一次施法行为与「全部回滚」合同不一致。

2. **作者给效果挂一个从未登记过的状态名**  
   在 Tag 表未冻结时，系统会当场造出新身份，而不是要求重启——与「不能热加新身份」合同冲突。

3. **作者只想改命中爆炸效果、不想动撞击效果**  
   热替换会把两个引用一起改掉。

4. **作者接受 AI 草稿并保存进 Mod**  
   工作台没有配置保存根目录；即便走服务，效果引用类改动也没有落盘映射；数值可能写进旁路补丁文件。

5. **新人打开「实时技能工作台」Showcase 指望看到火球变冰球**  
   注册表仍指向 Vignette 验收；仓库无证据目录；演示失败时画面可能继续播，冰色还可能只靠内部开关点亮。

---

## 5. 边界

**本审计覆盖**

- 交接文档列出的图基建文件、LSW Core、Workbench Mod、Showcase、registry、UAT 文档、证据工具  
- 合同文档与实现对照  
- 交叉审计对抗复核

**本审计不覆盖 / 不替代**

- 在本云环境重跑完整 Champion Raylib 录屏（交接已记 SIGSEGV；本轮以源码与仓库产物为准）  
- 替 #861 完成 S2–S4 废除 next-chain 源码（记为债，非本 PR 合并门槛的唯一条件，但须在 Epic 跟踪）  
- 修改生产代码（本交付仅为审计报告）

---

## 6. UAT（合入前必须成立）

```gherkin
Feature: 实时技能工作台热应用合同
  作为技能作者
  我希望在安全点一次提交要么全部生效要么全部回到旧定义
  以便试玩时不会出现「界面说失败但下一发已经半新半旧」

  Scenario: 一批候选中有一条失败则全部回滚
    Given 工作台已暂存两条均可分类为「下次施法生效」的改动
    And 第二条安全帧已打开
    When 提交时第二条改动在写入定义库时失败
    Then 运行时定义必须与提交前完全一致
    And 界面必须报告失败原因且 applied 计数为 0

  Scenario: 未知状态名不得热造身份
    Given 效果授予状态补丁引用了一个尚未登记的状态名
    When 系统对补丁做分类
    Then 结果必须是「需要重启」或明确拒绝
    And 状态名登记表不得新增该名字

  Scenario: 弹道字段互不串扰
    Given 作者只改 projectile.impactEffect 指向冰爆
    When 下次施法热应用成功
    Then 撞击效果字段保持原值
    And 命中爆炸字段变为冰爆

Feature: Champion 风格火球变冰球真机演示
  作为新玩家
  我希望在局内看到同一技能先发火球、热改后再发冰球并冻住靶子
  以便相信「编辑器热应用」是真的，而不是面板截图

  Scenario: 生产链路火→冰
    Given 启动 preset capability_standard_live_skill_workbench_raylib
    And 地图为 lsw_hot_apply_arena
    When 角色放出第一发技能
    Then 靶子受到火球对应结算且弹道表现为火
    When 系统经 LiveGasEditPipeline 完成下次施法热应用
    And 角色放出第二发技能
    Then 第二发弹道的效果引用已变为冰系
    And 靶子进入寒冰相关状态且生命值按真实结算变化
    And 仓库 artifacts 目录登记可读截图或录屏
    And showcase.registry 的验收测试名指向 Champion 路径而非 Vignette

  Scenario: 演示失败必须失败关闭
    Given Champion 演示在施法或热应用中抛错
    When 当前帧更新结束
    Then 演示不得继续以成功态绘制冰色弹道
    And 自动化验收必须判失败
```

---

## 附录 A — 交叉审计过程

| 路 | 焦点 | 结论摘要 |
|----|------|----------|
| Explore A | #861 | PARTIAL；FuncLib optional + S2–S4 债 |
| Explore B | #615 | FAIL；原子性/新 Tag/UI/AI/Save |
| Explore C | Showcase | NOT ACCEPTABLE |
| GeneralPurpose 对抗复核 | 12 条关键声明 | 10 VERIFIED / 2 NUANCE；无 Blocker 被推翻 |
| 审计人复读 | B1/B2/M1/Champion | 源码确认 |

合并就绪：**DO NOT MERGE**。

---

## 附录 B — 范围快照

- Diff 规模（相对 main）：约 144 files，+16346 / −683  
- 关键目录：  
  - `src/Core/GraphRuntime/`、`src/Core/NodeLibraries/GASGraph/`  
  - `src/Core/Gameplay/AI/{BehaviorTree,Fsm}/`、`Level/`  
  - `src/Core/Gameplay/GAS/LiveSkillWorkbench/`  
  - `mods/capabilities/live_skill_workbench/`  
  - `mods/showcases/capability_standard/CapabilityStandardLiveSkillWorkbenchShowcaseMod/`  
  - `showcase.registry.json`、`gitbook/acceptance/live-skill-workbench-uat.md`

### 合入前最低修复清单（按优先级）

1. **B1** Commit 全有或全无 + 回滚旧 snapshot  
2. **B2** 禁止 Classify/Commit 路径 `TagRegistry.Register`；未知 tag → `EngineRestartRequired`  
3. **M1** impact/hit 字段分离写入  
4. **M2** Classify 阶段校验热可编辑 fieldPath  
5. **Champion** 去掉吞异常与 `_hotApplied` 视觉旁路；registry/acceptance/artifacts 对齐真机路径  
6. **#623/#624/#620–622 UI 挂接** 或明确降级 Epic 范围并改合同（禁止口头「全做完」）  
7. FuncLib：catalog 已声明则缺文件必须失败关闭  

S2–S4 next-chain 废除可记独立债票，但不得再宣称 #861 已关。
