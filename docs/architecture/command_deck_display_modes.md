# CommandDeck Multi-Display Modes (WPK-3 / #602)

## 1. 概述

CommandDeck 用同一套 ability-backed 命令管线表达四类显示策略：全局、当前实体、聚合过滤、条件常驻。Web 只渲染 DataPlane payload；候选来源、过滤、聚合、路由、可见性全部是 profile 引用。玩家看到的是命令格与状态，不是四套后端。

## 2. 结构

```text
UI/command_deck_profiles.json
  -> CommandDeckProfileRegistry
      -> CommandDeckProjector
           (+ EntityCommandPanelSource / CollectionGas
            + FilterProfile 物化到 view.command_deck.filtered（不改写 sourceRef）
            + CastDispatch route over 全量聚合 members)
          -> CommandDeckWebUiTopicProducer (DataPlane)
              -> PanelKit manifest topic 订阅
```

| 模式 | 含义 |
|------|------|
| `global` | 无聚焦实体时仍从玩家/控制域来源显示命令 |
| `entity` | 显式 focused entity / view / command source |
| `aggregateFiltered` | collection/control-plane 候选按 aggregation+filter+route |
| `conditionalPinned` | visibilityCondition 满足时常驻，下一 revision 可移除 |

## 3. 详情

复用：`EntityCommandPanel` source 合同、`IEntityCommandPanelAggregationMemberSource`（聚合格成员集）、`AbilityAggregationProfile`、`CastDispatchProfile`（route）、`FilterProfile`、`ControlPlaneView`、`EntityCollectionStore`、WPK-1 PanelKit manifest。

投影行为：

- `filterProfileId`：投影前对 collection 成员做 FilterProfile 求值，把幸存者物化到 `view.command_deck.filtered`，再交给现有 panel source；原始 sourceRef / control-plane collection 不被改写；Web 不做过滤。
- `aggregateFiltered` + `routeProfileId`：从 source 的聚合成员集（非单一代表实体）经 CastDispatch 选激活目标，写入 payload 的 `routedOwner*`。
- 控制关系变化：下一 snapshot 的 ownerCount / revision 随 ControlPlaneView / collection 扩张或收缩。

禁止：formal selection authority；UI 内 build/train/research switch；缺 profile/topic/route/filter/source 时 silent fallback。

## 4. 场景

- C&C3 全局建造栏 + 超武常驻
- 星际当前单位技能格
- 帝国时代多建筑训练聚合（点击聚合格路由到更合适的兵营，而不是列表第一个）
- 群星/CK3 国家级条件常驻命令
- 训练-only filter：研究类命令从面板消失，训练类保留

## 5. 边界

- Build/Train/Research/Superweapon/Policy 是 category/aggregation/display 策略，不是新后端。
- CommandDeck 不拥有生产队列/科技/资源真相。
- 聚合格激活必须走 `routeProfileId`（CastDispatch）与显式成员集，不得固定第一个 member。
- Filter 只发生在投影侧 / 既有 collection source 路径，不在 Web 猜命令。

## 6. UAT

```gherkin
Feature: CommandDeck 多显示模式
  Scenario: 无聚焦实体时仍可使用全局命令
    Given 玩家没有当前聚焦实体
    And 玩家控制域提供建造能力
    When 全局 CommandDeck 投影刷新
    Then 玩家看到建造格且 revision > 0
    And 面板没有依赖 formal selection authority

  Scenario: 显式实体命令
    Given 玩家绑定一个工程单位为当前实体
    When 实体 CommandDeck 投影刷新
    Then 玩家看到该单位自己的命令

  Scenario: 聚合过滤显示 owner count
    Given 聚合 profile 与 route profile 已安装
    When 聚合 CommandDeck 投影刷新
    Then 玩家看到合并格的来源数量、状态与阻塞原因

  Scenario: 条件常驻随 revision 消失
    Given 常驻条件当前满足且超武格可见
    When 条件变为不满足
    Then 下一帧常驻区不再显示该命令且 revision 变化

  Scenario: 点击聚合格不会固定落到第一个来源
    Given 两个兵营都能训练同一步兵
    And 第一个兵营更远、第二个更近
    And route profile 为 nearest topN
    When 聚合 CommandDeck 投影刷新
    Then 该训练格的路由目标是更近的兵营

  Scenario: 过滤 profile 只留下训练类命令
    Given 面板候选同时有训练与研究命令
    And filter profile 只保留训练类
    When CommandDeck 投影刷新
    Then 玩家只看到训练格，看不到研究格

  Scenario: 控制关系变化后面板扩张或收缩
    Given 玩家控制一个可命令单位
    When 获得对另一单位的控制并刷新
    Then 相关命令格的来源数量增加且 revision 变化
    When 撤销该控制并刷新
    Then 来源数量回到原来且 revision 再次变化
```
