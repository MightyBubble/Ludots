Feature: 装甲阅兵里炮塔和炮管跟着底盘走，还能自己瞄准
  新玩家从「装甲阅兵」短剧里看懂多层挂接：车开过去时整车粘在一起；炮塔再自己转，炮管跟着朝前伸。

  Background:
    Given 我从 preset:capability_standard_attachment_vehicle_parade_raylib 启动装甲阅兵
    And 我看到青色大圈底盘、黄色炮塔和红色炮管点，以及顶部字幕

  Scenario: 底盘开动时炮塔和炮管贴着车走
    Given 字幕提示「底盘开动」
    When 底盘沿演练场向前开到约 20 米处
    Then 炮塔仍停在底盘正上方
    And 炮管仍挂在炮塔前伸位置，没有掉队或飘开

  Scenario: 炮塔独立转向后炮管跟着瞄准方向前伸
    Given 底盘已经停在阅兵终点
    When 炮塔转到朝上瞄准
    Then 炮管落到炮塔朝向的前方约 2.2 米处
    And 字幕显示「阅兵完成」
