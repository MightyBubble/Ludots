# rel-01 editor spec · 关系目录

> 编辑器实现任务书。编辑器需求见 [rel-01 UXD](../uxd/rel-01-catalog.md)；引擎侧见 [runtime spec](../spec-runtime/rel-01-catalog.md)。

## 1. 概述

九块词表编辑器实现：分页表单、引用索引、合并预览。

## 2. 设计

- 条目表单按块 schema 生成（字段与缺省同源目录结构定义）；空 id 条目不落盘。
- 引用索引扫描图节点关系符号字段（relationshipType/relationshipMode/metric/flag/relationship reason），保存时增量维护。
- 合并预览消费配置管线合并结果，标注每条目来源片段。

## 3. 精确语义与不变量

- 编辑器产物与手写 catalog 片段等价（同 schema）。
- 引用索引与图资产实际引用一致（改名影响清单可信）。

## 4. 依赖接口与验收

- 消费：目录结构定义、配置管线合并入口、图资产扫描。
- 验收：九块各建一例往返无损；改名影响清单与全量扫描一致。

**相关文档**：[rel-01 UXD](../uxd/rel-01-catalog.md) · [rel-01 runtime spec](../spec-runtime/rel-01-catalog.md)
