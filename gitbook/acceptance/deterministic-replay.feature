# deterministic_replay showcase 验收（Cucumber 描述；真机证据落 artifacts/acceptance/deterministic-replay/）

Feature: 确定性回放
  作为玩家，我希望录制一段操作后重放，世界演化与录制时逐 tick 一致，且回放不被实时输入污染。

  Scenario: 录制与重放一致
    Given 启动 preset deterministic_replay_showcase_raylib 且地图为 deterministic_replay
    When 我按 [Start record] 后按 [Nudge hero] 数次，再按 [Stop record]
    And 我按 [Play replay] 等待回放完成
    Then 面板显示 recorded end digest 与 playback digest 一致（MATCH 绿灯）

  Scenario: 输入隔离
    Given 回放进行中
    When 我按 [Nudge hero]
    Then 面板显示实时输入被拒绝，回放完成后 digest 仍然一致

  Scenario: 冷归档重放
    Given 已按 [Save archive]
    When 我重启进程后按 [Load latest archive] 并 [Play replay]
    Then 归档帧被重放且终点 digest 与录制终点一致

  Scenario: 逐帧步进
    Given 回放进行中
    When 我按 [Pause / Resume] 后多次按 [Step one frame]
    Then 世界按 tick 粒度演化且面板索引递增
