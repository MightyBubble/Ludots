# save_load showcase 验收（Cucumber 描述；运行验收由 Agent Bridge 驱动，证据落 artifacts/acceptance/save-load/）

Feature: 存档/读档闭环
  作为玩家，我希望存进真实磁盘槽位、继续游玩后读档，世界回到存档点，且冷启动后槽位仍在。

  Scenario: 存档点往返
    Given 启动 preset save_load_showcase_raylib 且地图为 save_load
    When 我按 [Nudge hero] 若干次，英雄离开初始位置
    And 我按 [Save via panel]，右侧槽位面板出现新 manual 槽
    And 我再按 [Nudge hero]，drift 数值大于 0 且连线伸长
    When 我按 [Restore latest]
    Then 英雄回到存档点，drift 显示 0 且状态行变绿

  Scenario: 排除实体不进档
    Given 已存在一个存档
    When 我按 [Spawn excluded decoy] 后再按 [Restore latest]
    Then Excluded Decoy 实体消失（SaveExcludedTag 语义）

  Scenario: 损坏槽位 fail-fast
    When 我按 [Corrupt latest slot] 后按 [Restore latest]
    Then 面板显示红色 section hash mismatch 错误，世界不被破坏

  Scenario: 冷启动闭环
    Given 已存在一个存档
    When 我退出进程并重新启动同一 preset
    Then 槽位面板仍列出该槽，且 [Restore latest] 可用
