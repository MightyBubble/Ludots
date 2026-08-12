# 实时技能工作台 UAT（Epic #615）

## 1. 概述

玩家/策划用工作台热调技能与规则、看效果链、试 AI 草稿并保存进 Mod。

## 2. 结构

改数值 → 预检/应用 → 施放 → 效果链 → 改属性 → AI 草稿试玩 → 保存。

## 3. 详情

覆盖子单 #616–#625 / #655。

## 4. 场景

```gherkin
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
  Scenario: 改名标签身份
    Given 运行中存在稳定标签 State.Burning
    When 策划试图换成未注册的新标签身份
    Then 工作台显示需要重启
    And 当前战斗继续使用旧规则
```

## 5. 边界

不做外部 LLM；Immediate 属性默认不落盘；禁止 ReloadConfigs 冒充热更。

## 6. UAT

见上 Cucumber；自动化见 `LiveSkillWorkbenchEpicAcceptanceTests` 与 Showcase acceptance。
