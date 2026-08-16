# gr-op-08 editor spec · 节点：关系系统

> 编辑器实现任务书。编辑器需求见 [gr-op-08 UXD](../uxd/gr-op-08-relationship.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-08-relationship.md)。

## 1. 概述

关系族目录与三级联动选择器从关系目录投影生成；组合门可视化。

## 2. 设计

- **目录条目**：描述符表扫描 21 行，写/读/管线三段分组映射（编辑器侧维护 op→段映射）。
- **联动选择器**：类型选择器数据源 = 关系目录类型清单；选定后度量/旗标选择器按该类型的目录条目收敛；换类型即清空下游符号字段。
- **组合门投影**：消费效果组合编译的 Unsupported 域元数据，在模板折叠视图内标红写侧节点。
- **reason 展示**：dst=reason 的 op 卡片固定显示记账说明文案。

## 3. 精确语义与不变量

- 联动候选与关系目录投影一致；不缓存目录快照跨会话。
- 组合门判定与效果组合编译的域元数据同源。

## 4. 依赖接口与验收

- 消费：描述符表、关系目录投影、效果组合 Unsupported 域元数据。
- 验收：换类型后旧度量残留被清；折叠视图内写侧标红；21 op 在三种 kind 下的可用性与掩码一致。

**相关文档**：[gr-op-08 UXD](../uxd/gr-op-08-relationship.md) · [gr-op-08 runtime spec](../spec-runtime/gr-op-08-relationship.md)
