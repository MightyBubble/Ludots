# Graph Editor Live Debug UAT

Feature: 夜袭旗舰波次触发与 Graph live debug

Scenario: 进圈后只生成第一波
  Given 游戏以 `AgentBridgeMod` 和 `NightRaidShowcaseMod` 启动，地图为 `night_raid`
  And `entities.query` 开局返回 0 个名称包含 `raider` 的实体
  When 玩家把英雄移动到 `raid_circle` 内
  Then `entities.query` 返回 3 个 `NightRaidRaider`
  And 三个位置为 `(420,-140)`、`(420,0)`、`(420,140)`，均在镜头内
  And `ludots.graph.debug` 返回 `on_raid_start` 的节点、游标和引脚变化

Scenario: 第一波清空后生成独立队伍第二波
  Given 第一波的 3 个实体在场
  When 玩家用右键处决清空第一波
  Then `kill_count` 变为 3
  And `entities.query` 返回 2 个 `NightRaidRaiderElite`
  And 两个位置为 `(760,-110)`、`(760,110)`
  And `on_wave1_cleared` 的 trace 显示 `EntityAliveCountChanged cross_below` 路径已执行

Scenario: 阈值触发 Boss 并完成胜利
  Given 第二波属于 team 3，且 `kill_threshold` 为 5
  When 玩家清空第二波，再处决 `boss_camp` 位置的 Boss
  Then `kill_count` 为 5，Boss 位置为 `(1000,0)`
  And Boss 死亡 trace 依次显示 `Suspended` 两拍后继续执行
  And 进度面板显示 `STAGE 5`，胜利面板显示 `VICTORY` 和 `HEROHEALTH 100`

Evidence:

- `manifest.json`：启动计划、桥接会话、截图与 trace 清单
- `trace.jsonl`：按增量 sequence 记录节点状态、游标和 pin 值
- `path.mmd`：从观察到胜利的驱动链路
- `artifacts/agent-bridge/shots/`：wave1、boss、victory 实机截图
