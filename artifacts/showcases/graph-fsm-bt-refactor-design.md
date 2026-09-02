# 图行为：BT / FSM L2 身份（设计纠偏）

## 1. 概述

本页冻结「外层是什么、叶子是什么、编辑器写哪份资产」。  
进度开账只认 `gitbook/architecture/graph-capability-status.md`；分层规矩只认 `gitbook/architecture/graph-layering-flow-and-behavior.md`；作者合同正本见 `gitbook/architecture/graph-bt-fsm-nested-func.md`。

**纠偏结论（本轮）：**

| 层 | 资产 | 宿主 |
|----|------|------|
| L2 BT | `AI/behavior_trees.json` | `BehaviorTreeWorld` |
| L2 FSM | `AI/hfsm.json` | `HfsmWorld` + `GraphProgramHfsmHost` |
| L1 叶子 | `action_lib.json` + `GAS/graphs.json` Script | 叶子程序 |

**禁止：** 把整树 / 整机写成 Script 糖当作者 SSOT；新增 `GraphKind.BehaviorTree` / `Fsm`。

**可留作回归：** `BtSequence` / `FsmState` 等糖与 `GraphBehaviorTreeHost` / `GraphFsmHost`——单元测试与编译降级，不是演武场 / 编辑器正门。

生产已删除 `Graph.BT.Tree.PatrolChaseAttack`、`Graph.FSM.Sentry` 外壳与 `bt.tree.patrolChaseAttack` ActionLib 条目。

---

## 2. 结构

```text
作者正门
  /bt-editor   → AI/behavior_trees.json
  /fsm-editor  → AI/hfsm.json
  /gas-graphs  → 叶子 Script 与其它函数图

运行
  BehaviorTreeWorld(bt.patrolChaseAttack) + ActionLib 叶子
  HfsmWorld(hfsm.sentry.scripted) + GraphProgramHfsmHost

回归（非 SSOT）
  GraphBehaviorTreeHost / GraphFsmHost + Script 糖 fixture
```

---

## 3. 详情

### 3.1 演武场诚实门

| 段 | 宿主 | 允许声称 |
|----|------|----------|
| BT featured | `BehaviorTreeWorld` + 叶子 Script | L2 行为树 |
| BT 10k crowd | 无图树（`ScriptSlices==0`） | 仅压测基线 |
| 哨兵 featured | `HfsmWorld` + 叶子 Script | L2 HFSM |
| 哨兵 10k crowd | 无图 HFSM（`LifecycleRuns==0`） | 仅压测基线 |

### 3.2 Bridge

- `GET/PUT /api/ai/behavior-trees?source=core|modId`
- `GET/PUT /api/ai/hfsm?source=core|modId`
- `GET /api/ai/topology-catalog`
- `GET /api/ai/action-lib?host=BehaviorTree|Hfsm`

### 3.3 糖宿主何时还能提

只在回归测试、消融对照、或明确标注「非作者 SSOT」时。不得写回生产 `graphs.json` 外壳。

---

## 4. 场景

1. 打开 `/bt-editor`，Core 数据源，改 `bt.patrolChaseAttack`，保存进 `assets/AI/behavior_trees.json`。
2. 双击 `bt.patrol` 叶子，进 Graph Editor 改 `Graph.BT.Leaf.Patrol`。
3. 跑 BT 演武场：前排巡逻 / 追击 / 攻击意图仍在；crowd 零 Script 切片。

---

## 5. 边界

- 不升 GraphKind；不新 opcode；不平行 VM。
- BT Parallel / 子树复用另线。
- 旧「整机 Script + GraphFsmHost 当旗舰」叙述作废。

---

## 6. UAT

```gherkin
Feature: L2 身份恢复

  Scenario: 演武场不再声称 Script 整树宿主
    Given 行为树演武场已启动
    Then Metrics Detail 含 BT L2
    And 主树来自 AI/behavior_trees.json 的 bt.patrolChaseAttack

  Scenario: 哨兵走 HFSM 拓扑
    Given 哨兵演武场已启动
    Then FeaturedUsesHfsmWorld 为真
    And Metrics Detail 含 HFSM L2
```
