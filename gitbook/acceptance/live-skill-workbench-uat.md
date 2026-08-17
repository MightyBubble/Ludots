# 实时技能工作台 UAT（Epic #615）

## 1. 概述

策划在运行中热改技能配置，玩家立刻用同一技能看到新效果。Showcase 验收故事：火球变冰球，头顶血条数字与冰冻倒计时可读。

## 2. 结构

真施放 → 工作台热改（下次释放） → 再施放 → 更高伤害 / 冰冻 / 蓝色表现 → HUD 可读。

## 3. 详情

覆盖子单 #616–#625 / #655。Showcase 走生产 `LiveGasEditPipeline` 与冠军技能施放管线，不做假伤害表、不做面板流水账。

## 4. 场景

```gherkin
Feature: 火球热改成冰球
  Scenario: 新玩家看懂一次热改前后的差别
    Given 场上站着射手和木桩，头顶显示满血 200/200
    When 射手放出第一发火球打中木桩
    Then 木桩血量变成 185/200
    When 编辑器把这次技能的命中改成冰球、伤害提高并带上冰冻
    And 射手再放同一技能
    Then 木桩掉更多血，身上出现冰冻，倒计时在跳
    And 玩家能从血条数字和冰冻倒计时看出数值变化

Feature: 实时调试技能数值
  Scenario: 策划修改火球伤害后试玩
    Given 玩家打开实时技能工作台并选中火球术
    When 策划把伤害改成 80 并暂存、预检、应用到下一次释放
    And 玩家再次释放火球
    Then 工作台显示下次释放生效
    And 当前对局不会被错误配置污染

Feature: 调试选中角色属性
  Scenario: 策划把选中单位生命调满
    Given 玩家选中了一个生命值为 25 的单位
    When 策划在工作台把生命值设为 100
    Then 该单位生命值变为 100
    And 正式 Mod 配置没有被修改

Feature: 查看一次技能的效果链
  Scenario: 玩家追踪一次技能释放
    Given 玩家开启了技能链路追踪
    When 玩家释放一次技能
    Then 工作台显示从释放到效果再到属性变化的时间线
    And 缓冲满时明确显示有事件被丢弃

Feature: AI 生成技能草稿并试玩
  Scenario: 玩家让 AI 生成一个冰冻技能
    Given 玩家打开工作台并选中一个角色
    When 玩家输入“做一个小范围冰冻技能”
    And AI 生成技能草稿并预检通过
    When 玩家点击试玩绑定
    Then 选中角色获得临时技能绑定

Feature: 保存试玩成功的技能为 Mod
  Scenario: 策划接受草稿并保存
    Given 策划已经预检通过一组可保存改动
    When 策划预览保存并确认写入当前 Mod
    Then 工作台显示将要写入的配置文件
    And 保存完成后配置文件可被重新加载

Feature: 不安全改动被拒绝
  Scenario: 改名破坏身份
    Given 运行中存在稳定标签 State.Burning
    When 策划试图换成未注册的新标签身份
    Then 工作台显示需要重启
    And 当前战斗继续使用旧规则
```

## 5. 边界

不做外部 LLM；Immediate 属性默认不落盘；禁止 ReloadConfigs 冒充热更；Showcase 不以 Skia 证据板或假伤害表冒充局内验收。

## 6. UAT

见上 Cucumber；自动化见 `LiveSkillWorkbenchEpicAcceptanceTests`、`LiveSkillWorkbenchShowcaseAcceptanceTests`、`LswFireToIceHotApplyTests`。Preset：`capability_standard_live_skill_workbench_raylib`。
