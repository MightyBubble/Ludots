# gr-op-13 editor spec · 节点：拓扑谓词

> 编辑器实现任务书。编辑器需求见 [gr-op-13 UXD](../uxd/gr-op-13-topology.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-13-topology.md)。

## 1. 概述

三件目录与 LoadViewer 搭档建议；纯读徽标静态标注。

## 2. 设计

- **目录条目**：描述符表扫描三行；kind 过滤按掩码。
- **搭档建议**：补全菜单对 KnowledgeHasProjection 的 `a` 引脚把 LoadViewer 置顶；建议只是排序。
- **徽标**："纯读"标注为编辑器静态映射。

## 3. 精确语义与不变量

- 目录可用性与掩码一致；建议不改变连线合法性判定。

## 4. 依赖接口与验收

- 消费：描述符表、值类型表。
- 验收：Query 图三件置灰；a 引脚补全首位是 LoadViewer。

**相关文档**：[gr-op-13 UXD](../uxd/gr-op-13-topology.md) · [gr-op-13 runtime spec](../spec-runtime/gr-op-13-topology.md)
