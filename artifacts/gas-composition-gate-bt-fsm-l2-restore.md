## GAS Composition Gate — Self Review

- **Task / Issue**: Restore L2 BT/FSM topology identity — outer shells in AI JSON, leaves in graphs.json Script; remove Script-sugar outer shells
- **Date**: 2026-09-01
- **Agent / Author**: cloud-agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（组合既有 Host + 配置路径，去掉错误塌缩）

结论: PASS

一句话理由: 不新增 GraphKind / opcode；复用 BehaviorTreeWorld / HfsmWorld / ActionLib / 叶子 Script；撤掉把整树整机写成 Script 糖的假作者身份。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| BT 外壳 | L2 | assets/AI/behavior_trees.json → BehaviorTreeWorld |
| FSM 外壳 | L2 | assets/AI/hfsm.json → HfsmWorld + GraphProgramHfsmHost |
| 叶子逻辑 | L1 | GAS/graphs.json Script via action_lib |
| 编辑器 | 作者面 | Bridge /api/ai/* + AiTopologyEditorPage |
| 假 Script 整树/整机 | 删除/降级 | Graph.BT.Tree.* / Graph.FSM.Sentry 外壳 |

### 3. Reuse list

- GraphBehaviorDefinitionLoader / GraphBehaviorCatalog
- BehaviorTreeWorld / HfsmWorld / GraphProgramHfsmHost
- GraphActionCatalog / action_lib.json / Graph.BT.Leaf.* / Graph.HFSM.*
- Integration showcase 已是正确路径

### 4. New Layer 0 ops

N/A

### 5. Transaction boundary

无

### 6. Config SSOT

行为配置落在: `AI/behavior_trees.json`、`AI/hfsm.json`、`GAS/action_lib.json`、`GAS/graphs.json`（仅叶子）

是否新增 JSON schema: NO（复用既有 schema）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未新增 GraphKind.BehaviorTree/Fsm

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（叶子 Script）或 behavior_trees.json 拓扑节点
