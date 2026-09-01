# BT / FSM 独立编辑器与函数图叶子

## 1. 概述

作者面拆成三套编辑器，各管各的：

| 编辑器 | 路由 | 只干什么 |
|--------|------|----------|
| **Behavior Tree Editor** | `/bt-editor` | 只画树：Sequence / Selector / Decorator，以及 Action / Condition 叶子门户 |
| **FSM Editor** | `/fsm-editor` | 只画状态：FsmState 臂，以及 FsmAction 门户 |
| **Graph Editor** | `/gas-graphs` | 改 Func / Event / Effect / Query 等函数图；**不**再放 BT/FSM 组合糖 |

叶子上的 Action、Condition、状态体，各自是一张 **Func Graph**。在 Graph Editor 里改逻辑；在 BT / FSM 编辑器里双击门户节点，跳进对应函数图。

运行时仍是一台 VM：装载前 `BehaviorGraphLeafWeaver` 把门户织进宿主 Script，再交给 `GraphBehaviorTreeHost` / `GraphFsmHost`。

本页是作者合同 SSOT。进度只认 [图能力唯一入口](graph-capability-status.md)。

## 2. 结构

```text
/bt-editor          外层 BT 拓扑
  BtAction / BtCondition.functionName ──织入──► Script Func Graph
  双击门户 ──navigate──► /gas-graphs?mod=&graph=<functionName>

/fsm-editor         外层 FSM 拓扑
  FsmAction.functionName ──织入──► Script Func Graph（保留 Halt）
  双击门户 ──navigate──► /gas-graphs?mod=&graph=<functionName>

/gas-graphs         Func / Event / Effect / Query …
```

## 3. 详情

| 糖 | 出现在 | functionName | 织入后 |
|----|--------|--------------|--------|
| `BtSequence` / `BtSelector` / `BtDecorator` | BT Editor | — | 树控制流 |
| `BtAction` / `BtCondition` / `BtLeaf` | BT Editor | 叶子 Script id | 剥掉 `HaltReturnInt` / `Return`，由 BT 状态尾声回报 0/1/2 |
| `FsmState` | FSM Editor | — | 相位臂 |
| `FsmAction` | FSM Editor | 状态体 Script id | **保留** `HaltReturnInt` |

- 装载：`BehaviorGraphLeafWeaver.ExpandDocuments`（在 `TriggerGraphInlineWeaver` 之后）。
- 残留 portal 编译失败关闭。
- React：`GasGraphEditorPage` 的 `dialect`（`bt` / `fsm` / `func`）过滤调色板与目录；门户带 `functionGraphPortal`。
- 样例（默认 Mod）：`Graph.BT.Tree.EditorSample`、`Graph.FSM.EditorSample`，叶子在 `Graph.Func.*`。
- **下一刀（未做）**：旗舰树整树改为只含组合 + 门户（仍是 Script，不新开 GraphKind）。

## 4. 场景

1. 打开 BT Editor，目录只见树壳；调色板只有 Sequence / Selector / Decorator / Action / Condition。
2. 双击 `BtCondition`（已填 functionName），跳进 Graph Editor 改感知逻辑；保存后外层仍指向同一 id。
3. 打开 FSM Editor，双击 `FsmAction` 进状态体函数图。
4. 装载时织入，真机仍走既有 Host。

## 5. 边界

- 不新开 opcode；不新开平行 VM。
- **BT / FSM 不是 GraphKind**：外层与叶子都是 `Script`；方言只活在编辑器 + Host + 织入糖上。
- 门户禁止出边；只用 `functionName`，不用 `graphId`。
- Graph Editor 调色板隐藏 BT/FSM 组合糖；错方言打开会按图 id 启发式跳到对应编辑器。
- 旗舰 `Graph.BT.Tree.PatrolChaseAttack` 仍可暂时内联叶子；迁门户另提交。

## 6. UAT

```gherkin
Feature: 行为树编辑器与函数图叶子

  Scenario: BT 编辑器只露树节点
    Given 我打开 /bt-editor
    When 我打开节点调色板
    Then 我只看到 Sequence、Selector、Decorator、Action、Condition 一类树节点
    And 我看不到普通函数图里的加减、查询节点

  Scenario: 双击 Action 进函数图编辑器
    Given BT 编辑器里有一个 Action，已经填好函数图名字
    When 我双击这个 Action
    Then 页面跳到 Graph Editor，并打开那张函数图

  Scenario: 织入后外层不再留门户
    Given 一张 BT 外层树，child 臂挂着 BtAction，functionName 指向一张 Script
    When 装载展开并编译
    Then 外层文档里没有 BtAction / BtCondition / BtLeaf 节点
    And 编译成功

Feature: 状态机编辑器与函数图状态体

  Scenario: FSM 编辑器双击进状态体
    Given FSM 编辑器里有一个 FsmAction，已经填好函数图名字
    When 我双击这个节点
    Then 页面跳到 Graph Editor，并打开那张状态体函数图
```
