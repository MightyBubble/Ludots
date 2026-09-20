# TriggerGraph 域扩展验收

## 1. 概述

验证技能域、Mod 域和跨地图 route 已接入现有 TriggerManager/TriggerGraph VM，且没有第二事件总线。

## 2. 结构

```text
abilities.json triggerGraphs -> map TriggerManager -> AbilityId filter
mod.json triggerGraphs       -> global TriggerManager -> ModId filter
map TriggerGraphs route=global -> global map index -> all loaded map mounts
```

## 3. 详情

- 技能图在地图注册时建立，施法事件按 `MapTrigger.AbilityId` 过滤。
- Mod 图在图程序注册后、`ModLoaded` 发出前建立，卸载先回收图。
- 跨地图广播保留来源 `MapId`、SourceEntity、TargetEntity 和 AbilityId。
- 重复 graph id、未知图、未知域、未知 route 均 fail-closed。

## 4. 场景

```gherkin
Feature: TriggerGraph 域扩展

Scenario: 技能多图按声明顺序挂载
  Given abilities.json 中一个技能声明三张 TriggerGraph
  When 一张活动地图完成加载
  Then 三张图在同一个 TriggerManager 中按声明顺序注册
  And 只有匹配 AbilityId 的技能事件触发它们

Scenario: Mod 生命周期图隔离
  Given Mod A 和 Mod B 都已解析
  When Mod B 发出 ModLoaded
  Then Mod A 的 ModId 过滤图不执行
  When Mod A 发出 ModLoaded
  Then Mod A 的图执行一次
  When Mod A 卸载
  Then Mod A 的图从 TriggerManager 中移除

Scenario: 跨地图 global route
  Given 地图 A 有 route=global 的事件图
  And 地图 B 已加载
  When 地图 B 发出该事件
  Then 地图 A 的 global 图收到保留来源上下文的事件
  When 地图 A 卸载
  Then 后续事件不再触发地图 A 的图
```

## 5. 边界

- `route=global` 只改变地图事件路由，不改变实体归属，也不创建新 World/MapSession。
- 技能图不在施法热路径动态增删触发器。
- Mod 图只由其声明的 Mod 拥有和回收。

## 6. UAT

| 检查 | 证据 | 状态 |
|---|---|---|
| Core 编译 | `src/Core/Ludots.Core.csproj` | 通过 |
| GasTests 编译 | `src/Tests/GasTests/GasTests.csproj` | 通过 |
| Ability triggerGraphs 顺序/重复 | `AbilityExecLoaderFailFastTests` | 通过（2） |
| Mod manifest triggerGraphs 顺序/重复 | `ModManifestJsonTests` | 通过（2） |
| global route / ability filter | `TriggerGraphMountTests` | 通过（3） |
| 实机 AgentBridge | `artifacts/agent-bridge/` | 未执行（阻塞真实运行闸门） |
| 实体域入口 | `map_trigger_night_raid` + `Graph.NightRaid.EntitySettlement` | 已接入，待实机验收 |
| Mod 覆盖 / Mod 图 / global route | `night_raid_override` + `showcase.registry.json` | 已接入，headless 通过，待实机验收 |
| 技能域可玩入口与截图 | `trigger-graph-domain-showcase-design.md` | 未宣称完成；共享火球四皮回归保持基线 |
