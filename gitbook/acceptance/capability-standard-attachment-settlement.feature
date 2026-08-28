Feature: 哨所大厅带着附楼和塔楼静立不晃
  新玩家从「哨所静物」短剧里看懂静态挂接：大厅不动时，附楼和塔楼始终停在声明的相对位置。

  Background:
    Given 我从 preset:capability_standard_attachment_settlement_raylib 启动哨所静物
    And 我看到白色大厅、青色附楼和黄色塔楼，以及顶部字幕

  Scenario: 附楼与塔楼相对大厅保持声明偏移
    Given 哨所组合已经装载完成
    When 场景静止若干拍
    Then 附楼停在大厅右侧约 7 米处
    And 塔楼停在大厅左后约 3.5 米、上侧约 6 米处
    And 字幕显示「静物验收」

  Scenario: 多拍重算后位置仍不漂移
    Given 附楼与塔楼已经落在声明偏移上
    When 再过几拍不做任何操作
    Then 附楼和塔楼的世界坐标保持不变
    And 它们仍挂在大厅上
