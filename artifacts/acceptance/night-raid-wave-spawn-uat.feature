Feature: 夜袭旗舰波次触发式刷怪

  Scenario: 进圈前没有预置敌人
    Given 玩家启动 map_trigger_night_raid_raylib 并停留在金圈外
    When 玩家查询当前地图实体
    Then 场上只有英雄、城门和 Boss 营地
    And 查询结果中没有 raider 或 elite

  Scenario: 进圈生成第一波
    Given 玩家已确认进圈前场上没有 raider
    When 玩家把英雄移动到 raid_circle 内
    Then TriggerGraph 将 stage 更新为第一波状态
    And 场上出现三名分散在金圈外沿的普通 raider
    And 截图中三名敌人都在镜头可见范围内且没有重叠

  Scenario: 清空第一波生成第二波
    Given 第一波三名普通 raider 已出现
    When 玩家右键处决三名普通 raider
    Then EntityAliveCountChanged(team 2, cross_below 0) 触发下一波
    And 场上出现两名独立模板的 elite raider
    And kill_count 继续累计而不是停在第一波

  Scenario: 累计击杀达到阈值后生成 Boss
    Given 普通 raider 与 elite raider 共五名敌人已被处决
    When kill_count 达到 kill_threshold
    Then stage 进入 Boss 状态
    And Boss 在 boss_camp 的视觉锚点 (1000,0) 生成
    And Boss 面板只在 Boss 实体出现后显示

  Scenario: Boss 阵亡完成流程
    Given Boss 已在营地出现
    When 玩家右键处决 Boss
    Then stage 进入胜利状态
    And 胜利面板显示
    And AgentBridge 截图、实体查询和面板状态来自同一目标进程
