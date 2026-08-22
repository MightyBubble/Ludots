Feature: 战局在存档、Replay 和断线恢复后保持一致
  Background:
    Given 我从 preset:persistence_online_replay_showcase_raylib 启动持久化与回放演示
    And 我看到地图 persistence_online_replay 和右侧 Persistence / Replay / Reconnect Lab 面板

  Scenario: 玩家从检查点恢复断线前的战局
    Given 我正在进行 Persistence RTS 训练战局
    And 屏幕显示当前战局编号和检查点 tick
    When 我让连接中断
    And 我点击 Reconnect
    Then 我看到战局从断线前的检查点继续
    And 屏幕显示恢复来源为 checkpoint recovery
    And 当前 tick 继续增加

  Scenario: 玩家把存档带过一次完整冷启动
    Given 我已点击 Checkpoint 并看到检查点 tick 和摘要
    When 我点击 Save 并退出游戏
    And 我再次从同一个 preset 启动并点击 Restore
    Then 我看到“从磁盘恢复”以及原检查点 tick
    And 存档路径显示为 %LOCALAPPDATA%/Ludots/persistence-online-replay/saves/manual/showcase.ldsave
    When 我继续推进战局
    Then 当前 tick 继续增加而不是回到默认初始值

  Scenario: 玩家播放 Replay 后得到同一结果
    Given 我已经记录了一段包含检查点和权威订单的战局
    When 我从检查点播放 Replay 到结束
    Then Replay 的 world digest 与连续运行结果一致
    And 每个权威订单按 tick 顺序出现
    And 我可以点击 Pause、Step 和 Reset，面板显示回放位置与已应用帧数
    And 回放期间我输入实时操作时，面板明确显示实时输入被隔离

  Scenario: 玩家发现缺帧时看到明确失败
    Given 我正在恢复一段断线战局
    When 我删除或打乱一帧权威订单
    Then 恢复被拒绝
    And 屏幕显示缺失或乱序的具体原因
    And 当前战局不会悄悄继续到未知状态
