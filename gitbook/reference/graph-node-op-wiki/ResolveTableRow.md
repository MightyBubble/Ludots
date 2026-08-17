# 按编号翻名册点将

报出 2 号，名册翻到那一行，册上的扣血照着木桩落下。


## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：通用查表
- 启动绑定：`capability_standard_graph_op_ResolveTableRow`
- 作者记号：`ResolveTableRow`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/ResolveTableRow.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ResolveTableRow.json` |
| 数据表 | `assets/GraphTables/lookup_tables.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 按 2 号点名翻到名册第 {result} 行，木桩血量从 {healthBefore} 掉到 {healthAfter}。
