# 【已不作 SSOT】图行为「真图化」跑偏记录（原 BT-1 / FSM-1a）

## 1. 概述

**本页不是正本。**  
角色 AI 的作者正轨是 **L2 拓扑**：`AI/behavior_trees.json` + `BehaviorTreeWorld`，`AI/hfsm.json` + `HfsmWorld`，叶子再调 L1。分层合同：[图怎么分层](../../gitbook/architecture/graph-layering-flow-and-behavior.md)。进度：[图能力唯一入口](../../gitbook/architecture/graph-capability-status.md)。扳回跟踪 [PR #1416](https://github.com/MightyBubble/Ludots/pull/1416)。

本页只保留历史：2026-08 有 agent 线把「真图」理解成「整树/整机摊进 Script 糖 + Graph\*Host」，并一度写进冻结口吻。那条线和分层原文冲突，**产品上判定为跑偏**。

## 2. 结构（跑偏时的主张，勿再当合同）

```text
当时错误地把下面当成正统：
  FsmState / Bt* 作者糖 → 整机/整树一张 Script
  GraphFsmHost / GraphBehaviorTreeHost → 旗舰大脑
  behavior_trees.json / hfsm.json → 降成「旧路径 / 无图压测」

正统应是：
  L2 JSON 拓扑 + BehaviorTreeWorld / HfsmWorld
  叶子 → ActionLib / Script（L1）
```

## 3. 详情（事实，不是授权）

### 3.1 仍在 main 上的代码（遗留）

- Script 作者糖：`BtSequence` / `BtSelector` / `BtDecorator` / `FsmState`（编译期降 L0，零新 opcode）
- 宿主：`GraphBehaviorTreeHost` / `GraphFsmHost`
- 演武场 featured 曾迁到糖图（如 `Graph.BT.Tree.PatrolChaseAttack`、`Graph.FSM.Sentry`）
- 测试：`GraphBehaviorTreeSugarTests`、`GraphBehaviorTreeHostTests`、`GraphFsmSugarTests`、`GraphFsmHostTests`
- 合入线索：PR #1261 / #1264 / #1415 等

这些证明「糖路径做过且还在」，**不证明「糖路径是正统」**。

### 3.2 为何算跑偏

1. 分层原文写明 L2 自己管粗结构，叶子调 L1——不是把粗结构消进一张 Script。
2. 把 L2 JSON 世界降成「旧路径」会逼作者面迁到 `/gas-graphs` 糖图，丢掉树/机拓扑编辑语义。
3. 产品裁定：必须是 L2；糖可留作 Script 流程组合，不得顶角色 AI 作者 SSOT。

### 3.3 怎么用本页

- 查历史、对照遗留代码：可以读。
- 派新实现、写验收、改注册表声称：以分层合同 + 能力页为准，**不要引用本页当冻结**。

## 4. 场景

1. 新人问「BT/FSM 正统是什么」→ 指分层合同与能力页 §3.3.0，不指本页。
2. 有人要继续扩 Graph\*Host 旗舰 → 停下，先看 #1416 是否已扳回 L2。

## 5. 边界

- 禁止再把本页标成「设计冻结 SSOT」。
- 禁止在能力页写「BT-1 / FSM-1a 图糖已收口」而不注明跑偏与扳回。
- Parallel / BT-2 仍是另线，与是否 L2 无关。

## 6. UAT（文档卫生）

```gherkin
Feature: 跑偏记录不再冒充正本

  Scenario: 接手的人找到正轨
    Given 我打开本页
    Then 标题或概述写明已不作 SSOT
    And 正轨链到分层合同与能力页
    And 扳回链到 PR #1416
```
