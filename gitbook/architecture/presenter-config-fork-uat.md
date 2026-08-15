# Presenter 配置分叉验收

三条配置要对齐：动画档案只保留状态片段、子展示体用名字标记作用域、同一子定义可以各写各的位姿。改配置必须能在游戏里看见对应变化。

## 1 概述

玩家进铁匠铺和动画验收场，不读类型名。铁匠铺门口左右两块金色场标大小不一样，是同一块模板写了两次位姿。开工时烟只跟着“干活”这一层走，拆工房时整棵树一起没。动画场里坦克履带、炮塔转向、后坐力分别跟三条具名通道走；档案里再写内置片段字段会直接加载失败。

## 2 结构

```text
动画档案
  stateClips：控制器打包状态 → 片段资产
  禁止 builtinClips

叠加通道（运行时具名槽）
  locomotion / aim_yaw / recoil

展示体树
  children[].scopeTag：名字字符串（structure / working）
  children[].overrides.transform：本实例位姿
  children[].overrides.params：本实例参数
```

铁匠铺场标复用定义 `blacksmith_field_marker`，左右两次实例只改 `overrides.transform`。作用域销毁走已有的 `DestroyPresenterScope`，按名字解析成整数 ID，不另开管线。

## 3 详情

- 动画档案只映射打包状态到片段。叠加层用具名通道，不再用内置枚举。
- 加载器遇到 `builtinClips` / `builtin_clips` 立即失败，不转写旧字段。
- 子实例位姿写在 `overrides.transform`：`localPosition`、`localRotation`（XYZ 角度）、`localScale`。C# 配置用 camelCase，蛇形字段直接拒绝。
- 旧的 `paramOverrides` 拒绝，必须写成 `overrides.params`。
- `scopeTag` 必须是语义字符串。数字 `101` 一类写法拒绝。旧编辑器整数 `100/200/300` 对应名字 `structure/parent_follow/bone_follow`；本仓库铁匠铺实际使用 `structure` 与 `working`。

## 4 场景

玩家打开铁匠铺展示：工房两侧各一块金色方块，左边小、右边大。改左边 `localScale` 再进场，只有左边变。

玩家打开动画验收场：坦克履带随 locomotion 起伏，炮塔随 aim_yaw 拧，开火时炮管随 recoil 后坐。面板上三条通道显示为 `locomotion` / `aim_yaw` / `recoil`，不是旧枚举名。

玩家给铁匠铺打上“干活”：烟出现。去掉“干活”：烟没了，工房还在。拆掉整座铺子：工房、烟、场标一起没。

## 5 边界

- 同一子定义两次实例，位姿必须来自实例覆盖，不能改共享 AssetBinding。
- 没有 `overrides.transform` 的子节点不挂覆盖组件，批量创建路径保持零分配。
- 有覆盖的子节点走逐个创建，覆盖写到该实体后再算世界变换。
- `scopeTag` 区分大小写、禁止首尾空格。
- 本仓库没有 LudotsJS 运行时。具名作用域的索引、去重、销毁以 C# 为准；JS 侧未实现不在本变更范围。
- 不把地图级参数覆盖套到嵌套子实例上。

## 6 UAT

```gherkin
Feature: 铁匠铺门口两块场标来自同一模板

  Scenario: 玩家看见左右场标大小不同
    Given 玩家进入铁匠铺展示
    Then 工房左侧有一块较小的金色方块
    And 工房右侧有一块明显更大的金色方块
    And 两块方块用的是同一套场标定义

  Scenario: 改子实例缩放只动其中一块
    Given 玩家进入铁匠铺展示
    When 作者只把右侧场标的 localScale 改大
    And 玩家重新进入同一场景
    Then 右侧方块变大
    And 左侧方块大小不变
```

```gherkin
Feature: 干活的烟和工房不在同一层

  Scenario: 开工只冒烟，工房还在
    Given 玩家进入铁匠铺展示
    And 铁匠铺还没开工
    Then 玩家看见工房，看不见烟
    When 铁匠铺进入干活状态
    Then 烟囱冒烟
    And 工房还在原地

  Scenario: 拆铺子整棵树一起没
    Given 玩家看见工房、场标和烟
    When 这座铁匠铺被拆掉
    Then 工房、场标和烟都消失
```

```gherkin
Feature: 动画验收场的三条具名通道

  Scenario: 玩家看见走、瞄、后坐分开动
    Given 玩家进入动画验收场
    Then 坦克履带随 locomotion 起伏
    And 炮塔随 aim_yaw 左右拧
    And 开火时炮管随 recoil 后坐一下
    And 面板上三条通道名字是 locomotion、aim_yaw、recoil

  Scenario: 档案里再写内置片段会进不去
    Given 作者在 animation_profiles 里写了 builtinClips
    When 游戏加载这份档案
    Then 加载失败
    And 失败信息要求只写 stateClips
```

通过标准：

- `BlacksmithShowcase_ChildTransformOverride_SameDefinitionTwoPoses`
- `DestroyPresenterScope_NamedTags_ReleasesOnlyThatScope`
- `AnimationAcceptanceMap_EmitsLayeredSkinnedSnapshot_ForTankAndHumanoid`
- `AnimationProfileConfigLoader_RejectsRemovedBuiltinClipsField`
- `Load_ChildOverrides_ParsesTransformAndParams_AndRejectsLegacyParamOverrides`
- `Load_RejectsNumericDefinitionIdScopeTagAndEventKeyAuthoring`
