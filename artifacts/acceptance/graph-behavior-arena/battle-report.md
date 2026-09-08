# 图行为演武场验收记录

## 概述

本次验收从 Raylib 真实入口启动行为树、HFSM 和联合演武场。联合场完成了运行中误点单步、暂停单步、关闭 L2、移除入侵者、扩大视野和放慢思考六项操作。

## 结构

- 主入口：`preset:capability_standard_graph_behavior_integration_raylib`
- 子入口：`preset:capability_standard_behavior_tree_arena_raylib`
- 子入口：`preset:capability_standard_hfsm_sentry_arena_raylib`
- 控制面：游戏内左上角 9 个按钮
- 运行状态：`BehaviorTreeWorld`、`HfsmWorld`、`GraphShowcaseMetrics`

## 详情

- 联合场进程 PID 为 `29216`，地图为 `capability_standard_graph_behavior_integration`，加载了目标 Mod 与 `AgentBridgeMod`。
- `/health` 的 `pumpCount` 从 `7312` 增长到 `7335`。
- 固定演武场镜头为 `Camera.Profile.FixedArena`，目标坐标保持 `(0,0)`，不会因窗口失焦漂离战场。
- 运行中点击“单步”后，界面明确显示“单步只在暂停时可用；先点‘暂停’。”
- 暂停后点击“单步”，决策波从 `707` 增加到 `708`。
- 关闭 L2 并调整场景后，界面显示 `L2 关`、`入侵者关`、视野 `7.0m`、思考间隔 `0.30s`，决策波保持 `708`。
- 行为树独立场和 HFSM 独立场均从各自 preset 启动，固定镜头下能同时看到战场、单位和控制台。

### 视野操作复验（2026-09-07）

- 修复后联合场进程 PID 为 `16948`，目标地图和加载 Mod 已由 `ludots.session.info` 核对；完整请求和响应追加在 `trace.jsonl`。
- 重置、暂停、将视野缩小到 `2.5m`，单步 25 次：BT 巡逻 6、追击 0、攻击 0；HFSM 待命 6。
- 将视野扩大到 `7.0m`：第 26 波 BT 追击 2、HFSM 警戒 5；第 27 波 HFSM 战斗 5。后续单步可观察到攻击、撤退和再次警戒。
- 第 50 波关闭 L2，再单步 5 次：决策波保持 50，BT 无追击和攻击，HFSM 状态保持不变。
- 当前封面和关闭 L2 截图已替换为本次进程截图。
- `GraphBehaviorSeparatedShowcaseAcceptanceTests`、`GraphBehaviorArenaAcceptanceTests`、`BehaviorTreeRuntimeTests`、`ScriptDialectL2AuthoringTests` 共 32 项通过。新增断言覆盖视野内外边界、扩大与缩小视野、攻击距离不随视野扩大，以及一分钟内两队持续响应。
- 万人段仍为 `scriptSlices=0` / `lifecycleRuns=0`；25 次合波最大 `1.525ms`，`over5ms=0`。

## 场景

```gherkin
Feature: 新玩家操作图行为演武场

  Scenario: 先暂停再观察一次决策
    Given 我进入 BT 与 HFSM 联合演武场
    When 我在自动运行时点击“单步”
    Then 控制台提示我先暂停
    When 我点击“暂停”后再点击“单步”
    Then 决策波只增加一次
    And 左侧行为树和右侧分层状态机各推进一次决策

  Scenario: 同场比较有无 L2 决策
    Given 入侵者正在穿过两道防线
    When 我点击“L2 开”关闭图行为
    Then 按钮显示“L2 关”
    And 两队守卫留在同一场景但不再响应入侵者

  Scenario: 调整战场条件
    Given 演武场已经暂停
    When 我移除入侵者并扩大视野和放慢思考
    Then 控制台显示入侵者已移除
    And 视野显示为 7.0 米
    And 思考间隔显示为 0.30 秒

  Scenario: 扩大视野后两队响应入侵者
    Given 我重置并暂停联合场，将视野缩到 2.5 米
    When 我单步 25 次
    Then 左侧 6 名守卫巡逻，右侧 6 名哨兵待命
    When 我将视野扩大到 7 米并单步
    Then 左侧 2 名守卫开始追击，右侧 5 名哨兵警戒
    When 我再单步
    Then 右侧 5 名哨兵进入战斗
```

## 边界

- 万人灰点是无图压测基线，不代表 featured 段没有执行叶子。
- BT 万人段必须保持 `ScriptSlices=0`；HFSM 万人段必须保持 `LifecycleRuns=0`。
- 关闭 L2 只停止两套 L2 决策，不会切换到 Script 整树。

## UAT

- 结果：通过。
- 截图：`artifacts/evidence/capability_standard_graph_behavior_integration/poster.png`
- 消融截图：`artifacts/evidence/capability_standard_graph_behavior_integration/l2-off.png`
- 行为树截图：`artifacts/evidence/capability_standard_behavior_tree_arena/poster.png`
- HFSM 截图：`artifacts/evidence/capability_standard_hfsm_sentry_arena/poster.png`
- 逐步记录：`artifacts/acceptance/graph-behavior-arena/trace.jsonl`
- 操作路径：`artifacts/acceptance/graph-behavior-arena/path.mmd`
