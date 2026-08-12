# PR #911 审计修复清单（#913 + #914 合并）

**审计 SSOT：** PR [#914](https://github.com/MightyBubble/Ludots/pull/914) → `docs/audits/pr911_funclib_actionlib_architecture_audit.md`  
**交接：** PR [#912](https://github.com/MightyBubble/Ludots/pull/912)  
**实现对象：** PR [#911](https://github.com/MightyBubble/Ludots/pull/911) tip `20bf1e031`  
**产品裁决（本清单）：** B4 选 **补回作者路径**，且 **每个 `GraphNodeOp` 必须有自动化测试 + Showcase 可见演示**（不得用「缩编写进合同」敷衍）。

本文件是修复 Epic 的工作清单。跟踪单：

| Issue | 角色 |
|-------|------|
| [#915](https://github.com/MightyBubble/Ludots/issues/915) | Epic：审计修复 + 全量 GraphNodeOp 测试/Showcase |
| [#916](https://github.com/MightyBubble/Ludots/issues/916) | 旁路：MobaDemo `Graph.Shield.Absorb` |
| [#917](https://github.com/MightyBubble/Ludots/issues/917) | 旁路：MathOpsChain 0-alloc |
| [#918](https://github.com/MightyBubble/Ludots/issues/918) | 旁路：GasTests 干净构建并行拷贝 |

---

## 1. 概述

两份交叉审计（#913 / #914）结论一致：**禁止合入**。幸福路径主体成立，但四条阻断踩红线；另有 5+ Major 与一批债。  
本清单把两份报告合成一份可执行待办，并加上全量图节点「测试 + Showcase」覆盖硬要求。

| 阶段 | 目标 | 合入门槛 |
|------|------|----------|
| P0 | 关 B1–B3、合同状态改实、M1–M3 | #911 才可再谈合入 |
| P1 | B4：44 无作者路径 opcode 补回前门 + 删掉的守卫测试恢复 | 作者面与文档一致 |
| P2 | 全量 `GraphNodeOp`：每节点 ≥1 自动化测试 + Showcase 注册可见 | Epic 关单条件 |
| P3 | Major 余项 / 合同 §6 UAT / ActionLib 真 Yield 叶子 | 合同「已落地」才可写回 |
| 旁路票 | MobaDemo Shield、0-alloc、干净构建并行拷贝 | **不夹带**进 #911 修复主 PR |

---

## 2. 结构

```text
P0 加载/边界/性能（阻断）
P1 作者面截肢修复（阻断 B4）
P2 全量 GraphNodeOp 测试 + Showcase 矩阵
P3 Major / UAT / 文档名实
旁路 预存在红灯（独立 issue）
```

---

## 3. 详情（可勾选）

### P0 — 阻断与合入硬门槛

- [x] **B1** FuncLib/ActionLib：catalog 已声明时，缺文件 **或** 合并结果 `[]` → 加载失败关闭（两 Loader）
  - 测试：缺文件、空数组、正常有条目
  - 证据源：#914 探针 P1/P1b；#906 修复清单 #7
- [x] **B2a** FuncLib 登记做可达性：沿 `InvokeScript`（含 `graphId`）/`Call` 递归，间接含 Yield → 拒绝（`GraphYieldPurityValidator`）
  - Script `graphId` 保留但加载期闭合；P2 形态登记失败
- [x] **B2b** LSW：FuncLib 目标图热替换含 Yield → 拒绝（传入真实 `GraphFunctionCatalog`）
- [x] **B3** Arena 门槛还原 avg/p95&lt;15 + `over5ms==0`；去掉 bootstrap 假理由
- [x] **合同状态** 改为「修复中（Epic #915）」；Score/Validation FuncLib 延后写明
- [x] **M1** 加载期拒绝非 Script FuncLib kind
- [x] **M2** `GraphActionCatalogLoader` 的 FuncLib 参数改为**必填**；测试覆盖同名冲突
- [x] **M3** `GraphRegistryTestBootstrap` 改走生产 Loader；方法名 `LoadCoreScriptsFuncLibAndActionLib`

### P1 — B4 作者面补回（选定路径：补回，不缩编）

- [x] 恢复被删守卫测试（SpatialQuery / Snap / FanOut / 关系度量；含 `GAS.GRAPH.ERR.SpatialQueryIncomplete`）
- [ ] 为附录 A.3 的 **44** 个无作者路径 opcode 补 FrontDoor/ControlFlow 作者路径（按族分期，见下表）
- [x] AllowTruncated/droppedOutput/validOutput：前门字段 + 失败关闭守卫已恢复（持续跟文档对齐）
- [ ] 文档：`graph-layering-flow-and-behavior.md` / `tag-display-lookup.md` 与真实前门一致

#### 44 opcode 分族（P1 交付切片）

| 族 | 成员 | 状态 |
|----|------|------|
| 浮点算术/比较 | Div/Min/Max/Clamp/Abs/Neg/CompareGtFloat、ConstBool | **done P1-A** |
| Tag / 显示 | HasTag、CompareEqEntity、SelectTagInMask、LookupTagDisplayToken | **done（合入）** |
| 查询扩展 | QueryRadius、QuerySortStable、QueryLimit、AggMinByDistance | **done P1-C** |
| 动态效果 / FanOut | ApplyEffectDynamic、FanOutApplyEffect*、FanOutDispatchEffect* | **done P1-D** |
| 关系变更 | Ensure/Remove/Set/Add/Get Metric、Set/Has Flag、HasLink、QueryBetweenPair | pending P1-E |
| 吸附 / 几何 | Snap*、LoadTargetPos*、ClampTargetToRange、IsPointInCircle | **done P1-F** |
| 事件 / 控制域 / 知识 | SendEvent、LoadEventPayload*、ControlDomain*、KnowledgeHasProjection、LoadContextSource、LoadContextTargetContext | **done P1-G** |

### P2 — 全量 GraphNodeOp：测试 + Showcase（硬要求）

数据驱动 SSOT（新建，禁止散落硬编码勾选）：

- [x] 新增 `assets/Configs/GAS/graph_node_op_coverage.registry.json`（SSOT）：每个 `GraphNodeOp` 一行：`op` / `authorableKinds` / `unitTestFilter` / `showcaseId` / `status`（初值：`GraphControlFlowCompiler*` 作者矩阵 + 44 无前门 opcode 标 `missing`；守卫见 `GraphNodeOpCoverageRegistryTests`）
- [ ] CI 守卫：registry 成员集合 == `GraphNodeOp` 枚举（**已落地**）；`status=covered` 要求测试名可解析且 Showcase 在 `showcase.registry.json` 注册（待 P2 全量覆盖）
- [ ] **每一个**可执行 opcode：
  - [ ] ≥1 条自动化测试（优先 FrontDoor 作者路径；VM-only 仅过渡期并标 `status=runtime-only`）
  - [ ] ≥1 条 Showcase 可见演示（可共用能力标准图行为沙盘分镜，但画廊文案必须说人话）
- [ ] 现有已有作者路径的 opcode：补齐 registry 映射，缺口补测/补 Showcase
- [ ] Showcase Detail / registry summary：**禁止**只堆 opcode 名；用玩家/作者场景句

### P3 — Major 余项与合同落地

- [ ] **M4** `gitbook/acceptance/graph-funclib-actionlib-uat.md`：合同 §6 逐条映射测试名；Effect→ActionLib 失败关闭测试；合同措辞与实现对齐
- [ ] **M5** 至少一条 `bt.*` ActionLib 叶子真含 Yield + 续跑验收；加载器内容层策略写清（或合同回写）
- [ ] Query L1 纳入 FuncLib 调用（#913 Major；#914 合同 §3.3「所有 L1」）
- [ ] 线性 `graphId` 拒绝 / Effect→ActionLib 名：补 FrontDoor 测试
- [ ] Minor：死 API `RequireId`、硬编码 ScriptKeys 数据化、加载顺序文档偏差、旧 GraphCompiler 文档图清理
- [ ] 合同状态在 P0–P3 完成后才改回「已落地」

### 旁路票（独立 Issue，不夹带）

| 项 | 说明 |
|----|------|
| MobaDemo `Graph.Shield.Absorb` | `LoadAttribute` 入参 `target`→应 `source`；5 生产测试红 |
| `MathOpsChain_Stress_ZeroAllocation` | 实测 ~880KB alloc |
| GasTests 干净构建 | 同名内容项并行拷贝 MSB3021/3027 |

---

## 4. 场景（业务）

1. 清单文件丢了 → 启动就报错，不要进关才崩。  
2. 纯算式不能偷偷跳进「等一拍」的动作；热改也不行。  
3. 思考波五毫秒闸门说真的，CI 不能装瞎。  
4. 文档写能写的节点，作者真能写出来；截断查询必须失败关闭。  
5. 画廊里每个图能力都有一幕能看懂的演示，不是术语清单。  
6. 巡逻叶子真的会跨拍接着走。

---

## 5. 边界

- 修复主 PR 只碰 #911 审计范围 + 图节点覆盖基建；不顺手改无关 Mod。  
- Champion 真机录屏债、#615 Save/UI 尾巴：可链到既有票，不扩写成本次唯一目标。  
- #909 已在 #911 内，不再单独合。  
- #910 vs #911 合同文件双源：维护者拍板 SSOT；修复期以实现分支合同页为准并改状态措辞。

---

## 6. UAT（合入 Epic 前）

沿用 #914 §6 Cucumber，并追加：

```gherkin
Feature: 每个图节点都有人能试、有测试守着
  作为作者与新玩家
  我希望每一个还能运行的图节点都既有自动测试、又能在画廊里看到一幕
  以便没有「引擎里有、谁也写不出、谁也看不见」的幽灵能力

  Scenario Outline: 节点覆盖登记完整
    Given 覆盖登记表列出了全部 GraphNodeOp
    And 节点 <op> 状态为 covered
    When 我查看它的自动化测试与 Showcase 条目
    Then 测试必须存在且可运行
    And Showcase 介绍必须用玩家或作者能懂的话描述会看到什么

    Examples:
      | op |
      | QueryRadius |
      | RelationshipSetMetric |
      | ApplyEffectDynamic |
      | SnapToNearestInCollection |
```

---

## 附录 — 复用清单（开工前）

- Registry：`GraphFunctionCatalog` / `GraphActionCatalog` / `GraphProgramRegistry` / `GraphIdRegistry`
- Pipeline：`ConfigPipeline` + 两 CatalogLoader + `GraphProgramAuthoringFrontDoor` + `GraphControlFlowCompiler*`
- 测试引导：生产 Loader（禁止平行 bootstrap）
- Showcase：`mods/showcases/capability_standard/*` + `showcase.registry.json`
- 合同：`gitbook/architecture/graph-funclib-actionlib-contract.md`
