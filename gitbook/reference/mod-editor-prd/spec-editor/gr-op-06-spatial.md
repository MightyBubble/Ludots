# gr-op-06 editor spec · 节点：空间查询

> 编辑器实现任务书。编辑器需求见 [gr-op-06 UXD](../uxd/gr-op-06-spatial.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-06-spatial.md)。

## 1. 概述

形状与管线目录从描述符表生成；容量策略、中心徽标、预览罩为编辑器侧附加投影。

## 2. 设计

- **目录条目**：描述符表扫描 12 行；kind 过滤按掩码（Query 图只出 Radius/Sort/Limit 三件）。
- **容量策略控件**：候选即 SpatialCapacityFlags 两值；缺省跟随图模板。
- **中心徽标**：编辑器侧静态映射表（锥/线/矩形→施法者；其余→目标点兜底），键为 op 名。
- **管线连线**：list 引脚类型 = TargetList；形状节点输出即 TargetList，聚合族（gr-op-09）的 list 输入同型可接。
- **预览罩**：消费地图渲染接口按形状参数画叠层，只读投影不落图数据。

## 3. 精确语义与不变量

- 目录可用性与掩码一致；连线类型判定与值类型表同源。
- 中心徽标静态表与 runtime 解析规则同步维护（规则变更时两处同改）。

## 4. 依赖接口与验收

- 消费：描述符表、SpatialCapacityFlags 枚举、TargetList 值类型、地图渲染接口。
- 验收：Query 图内锥形族不可落下；容量策略往返 JSON 无损；预览罩参数与节点字段同步。

**相关文档**：[gr-op-06 UXD](../uxd/gr-op-06-spatial.md) · [gr-op-06 runtime spec](../spec-runtime/gr-op-06-spatial.md)
