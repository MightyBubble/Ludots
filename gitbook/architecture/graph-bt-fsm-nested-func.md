# BT / FSM 外层与双击进函数图

## 1. 概述

作者面分两层：

- **外层**：只画行为树（Sequence / Selector / Decorator）或状态机（FsmState 臂），管粗结构。
- **双击叶子 / 状态体**：打开一张 **Script 函数图**（Func / Action），改真正逻辑。

运行时仍是一台 VM、一张织好的 Script 程序：`GraphBehaviorTreeHost` / `GraphFsmHost` 不变。织入发生在装载编译前，对偶 TriggerGraph 的 `InlineGraph`。

本页是作者合同 SSOT。进度只认 [图能力唯一入口](graph-capability-status.md)。

## 2. 结构

```text
外层 Script（BT 拓扑 / FSM 拓扑）
  BtLeaf.functionName ──织入──► Script 函数图（可 Yield）
  FsmAction.functionName ──织入──► Script 函数图（须 Halt）
编辑器双击 portal ──导航──► ?graph=<functionName>
```

## 3. 详情

| 糖 | Kind | functionName | 织入后 |
|----|------|--------------|--------|
| `BtLeaf` | Script | 叶子 Script id | 剥掉 `HaltReturnInt` / `Return`，由 BT 状态尾声回报 0/1/2 |
| `FsmAction` | Script | 状态体 Script id | **保留** `HaltReturnInt`（FSM 每波必须 halt） |

- 装载：`BehaviorGraphLeafWeaver.ExpandDocuments`（在 `TriggerGraphInlineWeaver` 之后）。
- 残留 portal 编译失败关闭。
- 独立叶子图可继续带 Halt，供旧 ActionLib / `BehaviorTreeWorld` 路径；织入 BT 时再剥。
- **下一刀（未做）**：`GraphKind.BehaviorTree` / `GraphKind.Fsm` 一等 Kind；旗舰树整树改为只含组合 + BtLeaf。

## 4. 场景

1. 作者打开行为树外层，只见 Sequence / Selector / Decorator / BtLeaf。
2. 双击 BtLeaf，进入 `Graph.BT.Leaf.SeeEnemy` 改感知逻辑，保存后外层仍指向同一 id。
3. 装载时织入，真机仍走 `GraphBehaviorTreeHost`。

## 5. 边界

- 不新开 opcode；不新开平行 VM。
- 本刀不新增 GraphKind（composition gate）；Kind 升格另开活。
- BtLeaf 禁止出边；禁止用 graphId 字段，只用 functionName。
- 旗舰 `Graph.BT.Tree.PatrolChaseAttack` 仍可暂时内联叶子；迁 BtLeaf 另提交。

## 6. UAT

```gherkin
Feature: 行为树叶子双击进函数图

  Scenario: 织入后外层不再留 BtLeaf
    Given 一张 Script 外层树，child 臂挂着 BtLeaf，functionName 指向叶子 Script
    When 装载展开并编译
    Then 外层文档里没有 BtLeaf 节点
    And 编译成功

  Scenario: 编辑器双击打开函数图
    Given 蓝图里选中带 functionName 的 BtLeaf
    When 我双击该节点
    Then 编辑器切到 functionName 对应的图
```
