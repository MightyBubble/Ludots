# gr-op-08 editor spec · 节点：关系系统

> 编辑器实现任务书。编辑器需求见 [gr-op-08 UXD](../uxd/gr-op-08-relationship.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-08-relationship.md)。

## 1. 概述

关系族目录与三级联动选择器从关系目录投影生成。写节点按图种掩码决定能不能落到画布。

## 2. 设计

- **目录条目**：描述符表扫描 21 行，写/读/管线三段分组映射（编辑器侧维护 op→段映射）。
- **联动选择器**：类型选择器数据源 = 关系目录类型清单；选定后度量/旗标选择器按该类型的目录条目收敛；换类型即清空下游符号字段。
- **效果图可写**：写节点的效果元数据是 GasTransactional。模板折叠视图不把它们标成无法执行。
- **reason 展示**：dst=reason 的 op 卡片固定显示记账说明文案。

## 3. 精确语义与不变量

- 联动候选与关系目录投影一致；不缓存目录快照跨会话。
- 画布能不能落下，与描述符表的图种掩码同源。效果模板引用写节点时，与效果计划的 GasTransactional 判定同源。

## 4. 依赖接口与验收

- 消费：描述符表、关系目录投影、效果组合 GasTransactional 元数据。
- 验收：换类型后旧度量残留被清；折叠视图里的关系写节点不标成无法执行；21 op 在三种 kind 下的可用性与掩码一致。

**相关文档**：[gr-op-08 UXD](../uxd/gr-op-08-relationship.md) · [gr-op-08 runtime spec](../spec-runtime/gr-op-08-relationship.md)
