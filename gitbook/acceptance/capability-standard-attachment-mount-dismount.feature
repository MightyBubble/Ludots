Feature: 乘员先上车跟车，再在车旁下车落位
  新玩家从「乘员上下车」短剧里看懂挂接生命周期：先挂到座位，车开时人不掉；再下车落到车旁一圈。

  Background:
    Given 我从 preset:capability_standard_attachment_mount_dismount_raylib 启动乘员上下车
    And 我看到青色载具圈、乘员色点，以及顶部字幕

  Scenario: 乘员上车后坐在载具座位上
    Given 字幕提示「上车」
    When 乘员完成上车
    Then 乘员贴在载具座位偏移处
    And 字幕切到「跟车」

  Scenario: 载具前移时乘员保持座位相对位置
    Given 乘员已经挂在载具上
    When 载具开到约 35 米处
    Then 乘员仍相对载具停在座位偏移，没有滑脱

  Scenario: 下车后落在载具周边而不是原地蒸发
    Given 载具已经开到终点
    When 乘员完成下车
    Then 乘员不再挂在载具上
    And 乘员落点在载具周围约 2.6 米的圆环上
    And 字幕显示「上下车完成」
