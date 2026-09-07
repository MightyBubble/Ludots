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

---

## 7. 可玩 Showcase 设计

### 一句话与目标用户

给第一次接触 Ludots 图行为的玩家一个可点击、可对照、能看懂决定原因的演武场。

### 主循环

- BT 场：入侵者穿过巡逻区，守卫由绿色巡逻切到黄色追击和红色攻击。
- HFSM 场：入侵者穿过岗哨线，岗哨在待命、警戒、战斗、撤退之间切换。
- 联合场：同一名入侵者同时经过两道防线，玩家能并排比较两种行为组织方式。
- 惊喜时刻：关闭 L2 后入侵者继续穿场，守卫不再响应；再次开启后，决策恢复。

### 消融对照

控制台的 `L2 开 / L2 关` 在同一场景切换。开启时，角色由 `BehaviorTreeWorld` 或 `HfsmWorld` 决策；关闭时只保留场景推进和基础巡逻，不运行另一套 Script 整树。

### 解释层

- 绿色表示巡逻或待命，黄色表示发现或警戒，红色表示攻击或战斗，蓝色表示撤退。
- 场上圆环显示当前感知半径，追击线显示角色正在响应哪名入侵者。
- 控制台显示各状态人数、入侵者数量、决策波次、本波耗时和万人段诚实门。

### 旋钮清单

| 旋钮 | 范围 | 玩家能看懂什么 |
|------|------|----------------|
| 自动运行 / 暂停 | 开 / 关 | 连续决策与定格观察的差别 |
| 单步 | 一次世界更新与一次决策 | 某个状态为何在下一波改变 |
| L2 决策 | 开 / 关 | 没有角色 AI 时，同一场景会失去什么 |
| 入侵者 | 开 / 关 | 刺激消失后角色是否回到常态 |
| 感知半径 | 1.5m 至 10m | 角色从多远开始响应 |
| 思考间隔 | 0.05s 至 1s | 决策频率如何影响响应速度 |
| 重置 | 默认状态 | 一键回到可重复起点 |

### 场景结构

- 主演示：`capability_standard_graph_behavior_integration_raylib`，左右并排显示 BT 与 HFSM。
- 子场景：`capability_standard_behavior_tree_arena_raylib` 和 `capability_standard_hfsm_sentry_arena_raylib`。
- 首屏引导：右侧控制台直接说明点击什么、观察什么，所有按钮都有稳定 UI id 供玩家和 Agent Bridge 使用。

### 门户资产

三个注册表条目分别指向各自 README、preset、验收测试和真实运行截图。截图由目标 Raylib 进程的 Agent Bridge 生成，运行参数仍取各 Mod 的正式配置和 L2 AI 资产。

### 反向 API 审计

| 需要的能力 | 复用位置 | 本次结果 |
|------------|----------|----------|
| 可点击控制台 | `IUiSurfaceHost` + `ReactivePage` | 共用控制台组件 |
| 场景状态读取 | `BehaviorTreeWorld` / `HfsmWorld` / `GraphShowcaseMetrics` | 直接读取正式运行态 |
| 画面反馈 | `DebugDrawCommandBuffer` | 感知圈、角色颜色、追击线 |
| 自动化操作 | Agent Bridge `ui.click` / `ui.tree` / `screenshot` | 真实进程验收 |

没有新增图种、opcode、平行 VM 或第二套输入系统。

### 交付边界与完成判据

本次把 BT、HFSM 和联合场三个已注册入口补成可点击产品；编辑器和 L2 作者合同不变。完成要求是三个 preset 均可干净启动，控制台可见，主循环、消融、参数旋钮和失败反馈可由 Agent Bridge 触发，并保留测试输出和真实截图。

```gherkin
Feature: 玩家比较 L2 行为树和分层状态机

  Scenario: 玩家关闭 L2 决策做同场对照
    Given 演武场正在自动运行且入侵者可见
    When 玩家点击“L2 开”按钮
    Then 按钮显示“L2 关”
    And 入侵者继续移动
    And 守卫不再产生新的追击或状态转换

  Scenario: 玩家暂停后单步观察决策
    Given 演武场正在自动运行
    When 玩家点击“暂停”再点击“单步”
    Then 世界只推进一次更新
    And 控制台的决策波次增加一次

  Scenario: 玩家在运行中误点单步
    Given 演武场仍在自动运行
    When 玩家点击“单步”
    Then 控制台提示先暂停
    And 系统不静默吞掉这次无效操作
```
