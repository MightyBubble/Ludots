Feature: NavMesh Bake Island 上的生产级 Mass Navigation 单位
  新玩家通过真实高度图、烘焙 NavMesh、Presenter 和 KayKit 动画，验证一条可以复制到新单位的生产链。

  Scenario: 两队单位在真实小岛上接受移动命令
    Given 我用 `--preset nav_bake_island_showcase_raylib --adapter raylib --build never` 启动游戏
    And 首屏地图是 `nav_bake_island`
    And 我能看到蓝队和红队的 KayKit 士兵，而不是占位方块
    And 两队单位都带有正式 MassNavigation agent、Presenter 和 Animator 配置
    When 我左键框选一队单位
    And 我右键点击岛另一侧的可通行地面
    Then 画面上出现该队的移动命令标记
    And 单位经过正式 `massNavigationMove` 与 MovePlan 链开始移动
    And 单位沿 NavMesh 可通行区域前进
    And 移动中的单位播放 `Walking_A`
    And 小地图上的单位位置与世界中的单位保持一致

  Scenario: 单位停止后回到待机动画
    Given 一队单位已经收到移动命令
    When 单位到达目标点并停止
    Then 该队单位的 Animator packed state 回到 `Idle`
    And 目标实体的位置已经改变到目标附近
    And Agent Bridge 日志没有吞掉的导航或动画错误

  Scenario: 配置缺少命名动画时启动失败可见
    Given 我把一个动画 clip locator 改成不存在的 KayKit 动画名称
    When 我重新启动该 showcase
    Then 启动或首次载入时明确报告缺失的动画名称
    And 系统不会静默改播另一个动画

  Scenario: 配置把动画指向错误模型时启动失败可见
    Given 我把动画 locator 指向不是 Presenter 实际使用的 mesh
    When Raylib 准备蒙皮批次
    Then 系统明确报告动画 locator 与载入模型不一致
    And 系统不会把错误模型当成正确的 Presenter 继续播放
