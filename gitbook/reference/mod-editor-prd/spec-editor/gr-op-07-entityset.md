# gr-op-07 editor spec · 节点：实体集查询

> 编辑器实现任务书。编辑器需求见 [gr-op-07 UXD](../uxd/gr-op-07-entityset.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-07-entityset.md)。

## 1. 概述

Query 图专用面板段与链式推荐从描述符表 TargetList 连通性自动生成。

## 2. 设计

- **目录条目**：描述符表扫描 14 行，全部 QueryOnly；当前图 kind 非 Query 时整组隐藏。
- **符号选择器**：属性/tag/模板/集合键四个选择器分别接各注册表投影；写回节点字符串字段。
- **链式推荐**：以 TargetList 类型连通性建候选——建集节点的 list 出引脚推过滤段，过滤推排序/聚合；推荐只是排序，不过滤作者自由。
- **旗标控件**：降序开关与 TeamIdSource 映射为节点字段编辑。

## 3. 精确语义与不变量

- 推荐候选集合与描述符表 TargetList 连通关系一致，不手编白名单。
- 符号选择器候选与注册表投影一致。

## 4. 依赖接口与验收

- 消费：描述符表、属性/tag/模板/集合键注册表投影、值类型表。
- 验收：非 Query 图整组隐藏；链式推荐覆盖全部 14 op 的合法后继；降序开关往返无损。

**相关文档**：[gr-op-07 UXD](../uxd/gr-op-07-entityset.md) · [gr-op-07 runtime spec](../spec-runtime/gr-op-07-entityset.md)
