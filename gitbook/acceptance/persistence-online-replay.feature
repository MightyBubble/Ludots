Feature: 战局在存档、Replay 和断线追回后保持一致

  Scenario: 玩家从检查点追回断线期间的战局
    Given 我正在进行 RTS 训练战局
    And 屏幕显示当前战局编号和检查点 tick
    When 我让连接中断
    And 连接恢复并追回断线期间的权威订单
    Then 我看到战局从断线前的检查点继续
    And 追回的单位位置与服务器一致
    And 屏幕显示恢复来源和追回帧数

  Scenario: 玩家播放 Replay 后得到同一结果
    Given 我已经记录了一段包含检查点和权威订单的战局
    When 我从检查点播放 Replay 到结束
    Then Replay 的 world digest 与连续运行结果一致
    And 每个权威订单按 tick 顺序出现

  Scenario: 玩家发现缺帧时看到明确失败
    Given 我正在追回一段断线战局
    When 我删除或打乱一帧权威订单
    Then 恢复被拒绝
    And 屏幕显示缺失或乱序的具体原因
    And 当前战局不会悄悄继续到未知状态
