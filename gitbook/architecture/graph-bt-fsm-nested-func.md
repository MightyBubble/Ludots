# BT / FSM 独立编辑器与函数图叶子

## 1. 概述

作者面拆成三套编辑器，各管各的：

| 编辑器 | 路由 | 只干什么 |
|--------|------|----------|
| **Behavior Tree Editor** | `/bt-editor` | 改 `AI/behavior_trees.json`：Sequence / Selector，以及 Action / Condition 叶子（挂 ActionLib） |
| **FSM Editor** | `/fsm-editor` | 改 `AI/hfsm.json`：Compound / Leaf 状态与转移；生命周期 / 条件挂 ActionLib |
| **Graph Editor** | `/gas-graphs` | 改 Func / Event / Effect / Query 等函数图；**不**再当 BT/FSM 外壳编辑器 |

叶子上的 Action、Condition、状态体，各自是一张 **Func Graph**（今日落地为 `Script`，经 `action_lib.json` 绑定）。在 Graph Editor 里改逻辑；在 BT / FSM 拓扑编辑器里双击叶子，跳进对应函数图。

运行时分层（合同见 [图分层](graph-layering-flow-and-behavior.md)）：

- **L2**：行为树 / 状态机 —— 粗拓扑 SSOT 在 `AI/behavior_trees.json` / `AI/hfsm.json`，由 `BehaviorTreeWorld` / `HfsmWorld`（+ `GraphProgramHfsmHost`）驱动。
- **L1**：叶子上的 **Func Graph**（今天落地为 `Script`）—— Action / Condition / 状态体的真正逻辑。
- **L0**：共享指令机。

**禁止**把整棵树 / 整台状态机写成一张 Script 糖文档当作者 SSOT；也**禁止**新增 `GraphKind.BehaviorTree` / `Fsm`。`GraphBehaviorTreeHost` / `GraphFsmHost` 与 `BtSequence` / `FsmState` 等糖只作编译降级回归，不是演武场 / 编辑器正门。

本页是作者合同 SSOT。进度只认 [图能力唯一入口](graph-capability-status.md)。

## 2. 结构

```text
/bt-editor          AI/behavior_trees.json
  Action / Condition.action ──ActionLib──► Script Func Graph
  双击叶子 ──navigate──► /gas-graphs?graph=<action_lib.graph>

/fsm-editor         AI/hfsm.json
  onEnter / onTick / onExit / condition ──ActionLib──► Script
  双击叶子 ──navigate──► /gas-graphs?graph=<action_lib.graph>

/gas-graphs         Func / Event / Effect / Query …（叶子与其它函数图）
```

## 3. 详情

| 层 | 资产 | 宿主 |
|----|------|------|
| L2 BT | `assets/AI/behavior_trees.json` | `BehaviorTreeWorld` |
| L2 FSM | `assets/AI/hfsm.json` | `HfsmWorld` + `GraphProgramHfsmHost` |
| L1 叶子 | `GAS/action_lib.json` + `GAS/graphs.json` Script | 叶子程序 |
| Bridge | `GET/PUT /api/ai/behavior-trees`、`/api/ai/hfsm`；`GET /api/ai/action-lib`；`GET /api/ai/topology-catalog` | 拓扑 CRUD |

- Schema：`behavior_trees.schema.json` / `hfsm.schema.json`。
- 装载：`GraphBehaviorDefinitionLoader` → `GraphBehaviorCatalog`（生产路径，与演武场一致）。
- 默认数据源：Core（仓库根 `assets/AI/`）；有 `assets/AI/*.json` 的 Mod 也会出现在目录里。
- 糖宿主 / 门户织入（`BehaviorGraphLeafWeaver`、`GraphBehaviorTreeHost`、`GraphFsmHost`）保留为**回归**，不得再当 featured 作者面。

## 4. 场景

1. 打开 `/bt-editor`，数据源选 Core，目录出现 `bt.patrolChaseAttack`；调色板语义是树节点，不是加减查询。
2. 双击挂了 `bt.seeEnemy` 的 Condition，跳进 Graph Editor 打开 `Graph.BT.Leaf.SeeEnemy`；保存后外层仍指向同一 ActionLib 名。
3. 打开 `/fsm-editor`，编辑 `hfsm.sentry.scripted` 的 combat 生命周期；双击 `onTick` 进叶子函数图。
4. 跑演武场：BT 走 `BehaviorTreeWorld`，哨兵走 `HfsmWorld` + 叶子 Script。

## 5. 边界

- 不新开 opcode；不新开平行 VM（L0 共用）。
- **BT ≠ FSM ≠ Func Graph**：作者面、拓扑语义、宿主分派各走各的；共享的只是 L0 指令机。
- **BT / FSM 不是 GraphKind**：它们是 L2 行为调度；Func Graph 才是 L1（今日为 Script）。
- 叶子只用 ActionLib **名字**，不用硬编码 graphId。
- Graph Editor 不再以 `Graph.BT.Tree.*` / `Graph.FSM.*` 为作者外壳目录。
- 一期边界：BT Parallel 不支持；子树跨图复用等归 BT-2，**不要和本轮 L2 身份恢复捆在一起**。

## 6. UAT

```gherkin
Feature: 行为树拓扑编辑器

  Scenario: 打开就能改真正的树资产
    Given 我打开 /bt-editor
    And 数据源是 Core
    When 我选中 bt.patrolChaseAttack
    Then 我看到 Sequence / Selector 与挂着 ActionLib 的叶子
    And 保存写入的是 AI/behavior_trees.json，不是一张 Script 糖图

  Scenario: 双击叶子进函数图
    Given 树上有一个 Condition，已经挂好 bt.seeEnemy
    When 我双击这个叶子
    Then 页面跳到 Graph Editor，并打开 Graph.BT.Leaf.SeeEnemy

Feature: 状态机拓扑编辑器

  Scenario: 编辑哨兵机并进叶子
    Given 我打开 /fsm-editor 并选中 hfsm.sentry.scripted
    When 我双击 combat 的 onTick
    Then 页面跳到 Graph Editor，并打开对应的叶子函数图
```
