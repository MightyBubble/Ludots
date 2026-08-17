# 读名册上的星数

点到 2 号那行，册上记着三颗星，照数挂印。


## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：通用查表
- 启动绑定：`capability_standard_graph_op_TableReadInt`
- 作者记号：`TableReadInt`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/TableReadInt.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/TableReadInt.json` |
| 数据表 | `assets/GraphTables/lookup_tables.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 名册这行攒了 {result} 颗星，木桩血量从 {healthBefore} 掉到 {healthAfter}。
