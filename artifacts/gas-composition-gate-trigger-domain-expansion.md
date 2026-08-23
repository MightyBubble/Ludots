# GAS Composition Gate - TriggerGraph Domain Expansion

## 1. 概述

本片把 TriggerGraph 扩展到 ability、mod 和 global map route，并把 entity attachment 聚落入口接入现有图执行器。没有新增 profile DSL、第二事件总线或第二 VM。

## 2. 结构

```text
abilities.json.triggerGraphs -> AbilityDefinitionRegistry -> TriggerManager AbilityId mount
mod.json.triggerGraphs       -> ModLoader -> TriggerManager ModId mount
map TriggerGraphs route=global -> TriggerManager global map index
EntityTemplate.TriggerGraphs -> EntityTriggerGraphMounts -> attachment scope
```

## 3. 详情

- 技能图按声明顺序加载，空白、重复和未知图名失败关闭；不在施法热路径增删挂载。
- Mod 图在 `ModLoaded` 前登记，按 `ModId` 过滤，卸载前回收。
- global route 只扩展地图事件接收范围，保留来源 `MapId`、实体和技能载荷。
- 实体图仍属于所属 `World/MapSession`；attachment 只定义成员身份和位姿。
- 所有写世界动作仍通过现有 GraphRuntimeApi 原子 op 组合，未增加声明式 profile。

## 4. 场景

1. 技能完成事件只触发对应 AbilityId 的图。
2. Mod A/B 同时加载时，ModLoaded 事件只触发拥有者图。
3. 地图 B 的事件可到达地图 A 的 global mount，地图 A 卸载后停止到达。
4. 夜袭英雄实体出生时写入所属地图的 `entity_ready`，证明实体域真实挂载。

## 5. 边界

- 不改变 GAS 一步滞后和 Presenter 只读合同。
- 不创建实体级时钟、第二 MapSession、第二事件总线或 fallback 路由。
- 共享火球四皮入口不被新技能图强制改写；专用技能域可玩入口和 AgentBridge 证据仍是后续收口项。

## 6. UAT

```gherkin
Feature: TriggerGraph 域扩展保持单一执行路径

  Scenario: 一个技能可以挂多张图
    Given 一个 ability 定义按顺序声明多张 TriggerGraph
    When 地图完成加载
    Then 所有图在同一个 TriggerManager 注册
    And 施法事件只按 AbilityId 路由到这些图

  Scenario: Mod 覆盖与 global route 可回收
    Given NightRaidOverrideMod 声明 Mod 图并挂一张 global 地图图
    When ModLoaded 或任意活动地图 MapLoaded
    Then 对应图收到事件
    When Mod 或地图卸载
    Then 后续事件不再触发已回收图

  Scenario: 实体 attachment 仍是所属地图的子世界
    Given 夜袭英雄模板声明 entity TriggerGraph
    When 英雄出生
    Then 图在出生当拍写入所属地图变量
    And 没有创建第二 World 或 MapSession
```
