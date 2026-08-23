# 实体系统验收

## 1. 概述

本页把实体关系和实体挂接的验收写成玩家、Mod 作者或关卡作者可以观察的结果。它不以内部组件是否存在作为唯一通过标准；代码测试路径只作为证据。

## 2. 结构

```text
关系类型出生状态 → 关系实体可用 → 关系状态可观察
挂接请求 → 写权/成员身份交接 → 跟随或解除挂接 → 继续移动
失败预检 → 明确错误 → 原状态保持不变
```

## 3. 详情

验收入口：

- 关系类型模板：`src/Tests/GasTests/Association/RelationshipTypeTemplateTests.cs`
- 实体挂接事务和失败路径：`src/Tests/GasTests/Effect/EntityAttachmentTests.cs`
- 挂接位置同步和孤儿清理：`src/Tests/GasTests/Effect/AttachmentPositionSyncSystemTests.cs`
- 真实 MassNavigation authority 链：`src/Tests/PresentationTests/MassNavigation/MassNavigationAttachedAuthorityTests.cs`

## 4. 场景

```gherkin
Feature: 关系类型进入一场游戏

  Scenario: 关系出生状态可被玩法使用
    Given Mod 作者注册了一个带出生模板的关系类型
    And 地图或玩法创建了两名参与者之间的该关系
    When 游戏加载并物化关系实体
    Then 关系实体带有作者声明的初始状态
    And 关系身份仍由游戏运行时管理
    And 玩家能在关系 Showcase 中看到关系状态变化

Feature: 玩家让乘客登上载具

  Scenario: 乘客登车后跟随载具
    Given 玩家控制一辆正在移动的载具
    And 一名乘客拥有自己的导航目标
    When 玩家执行登车操作
    Then 乘客停止占用独立导航成员槽位
    And 乘客跟随载具保持局部偏移
    And 载具和乘客不会同时争抢乘客的最终位姿

  Scenario: 乘客下车后继续行军
    Given 乘客正在跟随载具
    When 玩家执行下车操作并选择周界落位
    Then 乘客落到确定的周界位置
    And 乘客恢复导航成员身份
    When 玩家给乘客下达新的移动命令
    Then 乘客从下车位置继续移动

Feature: 错误操作不会破坏当前游戏状态

  Scenario: 挂接预检失败
    Given 父实体没有有效世界位姿，或挂接会形成环
    When 玩家或 Mod 发起挂接
    Then 系统显示明确的失败原因
    And 原来的父子关系、位姿和导航状态保持不变

  Scenario: 事务中途失败
    Given 挂接已经暂存但世界状态在提交前发生变化
    When 事务提交
    Then 提交失败并回滚完整成员快照
    And 玩家不会看到半挂接、半解除挂接或重复导航成员
```

## 5. 边界

- 这些场景验证 Core 真实链路，不用假状态面板代替实体世界结果。
- 关系 Showcase 的具体视觉表现仍由 registry 中的 Showcase 和对应 acceptance test 负责。
- 本页不宣称通用关系玩法、编队玩法或 Presenter 骨骼挂载已经被实体挂接合同覆盖。

## 6. UAT

通过条件：

- 关系实体出生状态与模板一致，运行时身份没有被作者配置接管。
- 挂接、跟随、解除挂接和恢复移动均走正式 Core 链路。
- 任一失败路径都明确失败，并恢复完整原状态。
- 自动化验收与本页场景保持一一对应，不以历史 artifact 或截图单独作为通过依据。
