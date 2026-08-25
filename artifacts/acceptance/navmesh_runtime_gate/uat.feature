# 语言: zh-CN
# NavGate（navmesh 运行时更新·隘口封锁）玩家视角验收
Feature: navmesh 运行时增量更新——隘口封锁

  Scenario: 小队行军并在城门落下时改道（主循环）
    Given 我通过启动预设 nav_gate_raylib 进入 nav_gate_valley 地图
    And 一支 8 人小队在 A 营集结并向 B 营行军，绿色路径折线可见
    When 小队接近隘口时城门落下，navmesh 被挖出红色空洞
    Then 隘口相邻瓦片出现橙色描边（增量重烤进行中）
    And 全队路径折线变为黄色并绕开城门翻山
    And 所有成员最终抵达 B 营，B 营抵达进度环闭合为绿色

  Scenario: 冻结重烤即为消融对照
    Given 同一场景小队正在行军
    When 我按下 F 冻结增量重烤，再让城门落下
    Then navmesh 不更新，旧绿色路径直线穿过红色城门
    And 我再按 F 解冻，瓦片橙色亮起且路径立刻弹开绕行

  Scenario: 手动障碍与半径旋钮
    Given 我在游戏中按 R 可切换手动障碍半径 12m/24m/48m
    When 我按 P 在相机脚下放下障碍
    Then navmesh 立刻重烤出对应大小的洞
    And 我按 O 清除全部手动障碍后洞被填回

  Scenario: 无人操作的自动巡演
    Given 我启动后不做任何操作
    Then 集结→行军→封锁→绕行→抵达→返程的完整循环自动上演并无限重复
