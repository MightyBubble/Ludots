# GAS + Graph 修复计划落地后架构审计（进行中）

**对象：** `origin/main` @ `46fcd9dcda`  
**需求：** [`s_plan_landed_audit_handoff.md`](s_plan_landed_audit_handoff.md)  
**本页状态：** 先写玩家门「一节点 / 一展厅 / 一地图」。其余领域未合成，**尚无总 Verdict**。

---

## 1. 概述（仅玩家门）

产品裁定是：玩家点的是「这一刀怎么算」，不是「进属性族大杂烩自己翻」。对照当前主线，**登记层已经做到一节点一展厅一地图**。布景可以复用同一套站位，但开图编号和展厅入口不共用。

`None` 不是玩家节点，不进展厅。八间家族大杂烩仍在仓库里，状态是退役，启动器不再给入口。

---

## 2. 结构

```text
已写：玩家门（一节点一展厅一地图）
未写：查询口 / 退役清表 / 血条演戏 / 属性收口 / S12 / S13 / S14 / 前序复验 / 总 Verdict
```

---

## 3. 详情：一节点一展厅一地图

对照 `GraphNodeOp` 枚举、覆盖表、分镜、地图、薄入口、登记表、启动器绑定与预设。

| 集合 | 数量 | 一对一？ |
|------|------|----------|
| 枚举成员（含 `None`） | 121 | `None` 无展厅，其余 120 全有 |
| 覆盖表 `covered` | 120 | 每条 `showcaseId = capability_standard_graph_op_{Op}`，无共用 |
| 分镜 `Vignettes/{Op}.json` | 120 | 与覆盖表集合相同 |
| 地图文件与 `Id` | 120 / 120 不重复 | `capability_standard_graph_op_{Op}` |
| 薄入口 Mod | 120 | `game.json` 的 `startupMapId` 等于自己的地图 Id |
| 登记表 `capability_standard_graph_op_*` | 120，全是 `active` | 每条有 binding 和 preset |
| 启动器 binding / preset | 120 / 120 | 无家族 binding、无家族 preset |
| 家族 `capability_standard_graph_ops_*` | 8 | 全是 `retired`，binding/preset 皆空 |

开图：共享宿主 `CapabilityStandardGraphOpsNodeGalleryMod` 从 `startupMapId` 解析节点名，再 `LoadExclusiveMap`。换一间短剧会换一张地图，不会跟隔壁共用同一个地图 Id。

布景不是 120 套完全不同的战场。实体站位只有 **28** 种布局：加减乘除那一类大量复用「施法者 + 木桩」；关系族复用五人好友链；筛人族复用十三人花名册。这是同一间摄影棚换海报，不是两间短剧开同一张地图。

生成器 `scripts/generate-graph-op-node-galleries.py` 是这些入口、地图、登记表的唯一写入源。宿主 Mod 自己不是启动器 binding。

---

## 4. 场景

1. 打开启动器：能看到 120 个中文短剧名，不是自动只进某一关。  
2. 点「两段伤害叠在一起」：开的是 `capability_standard_graph_op_AddFloat` 这张地图，场上有施法者和木桩。  
3. 再点另一间加减类短剧：地图 Id 换了，人按那张地图重新刷；站位可能看起来一样。  
4. 退役的「属性/效果模板图节点」等八间大杂烩：登记表划掉，启动器没有可复制的启动命令。

---

## 5. 边界

本页只回答玩家门的 1:1:1。不回答血条是不是演戏、查询口能不能偷挂起、分层有没有拆完。

---

## 6. UAT（仅本页范围）

```gherkin
Feature: 一个节点一间短剧一张地图
  作为新玩家
  我希望点某个图节点时只进它自己的短剧
  以便我看懂这一刀，而不是走进一整族大杂烩

  Scenario: 每个还能运行的节点都有自己的门
    Given 引擎里除了空操作以外有 120 个图节点
    When 我打开启动器
    Then 我能看到 120 个对应的中文短剧
    And 每一间都有自己的地图编号
    And 退役的八间家族大杂烩不再作为可点入口

  Scenario: 换一间短剧就换一张地图
    Given 我刚看完「两段伤害叠在一起」
    When 我再点另一间短剧
    Then 游戏打开的是另一张地图编号
    And 不得继续使用上一间的 startupMapId
```

本页对照：过（登记层）。布景复用不构成失败。
