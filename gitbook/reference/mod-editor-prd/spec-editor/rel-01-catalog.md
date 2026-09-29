# rel-01 editor spec · 关系目录

> 编辑器实现任务书。编辑器需求见 [rel-01 UXD](../uxd/rel-01-catalog.md)；引擎侧见 [runtime spec](../spec-runtime/rel-01-catalog.md)。

## 1. 概述

四块词表编辑器实现：分页表单、引用索引、合并预览。类型 / 度量 / 旗标写入 `catalog.json`，知识授予写入 `projection.json`。

## 2. 设计

- 条目表单按块 schema 生成（字段与缺省同源目录结构定义）；缺 id 的条目不允许保存。
- 引用索引扫描地图关系边、图节点关系符号字段（relationshipType / metric / flag）和技能、AI、命令、表现里的关系类型名，保存时增量维护。
- 合并预览消费配置管线合并结果，标注每条目来源片段。

## 3. 精确语义与不变量

- 编辑器产物与手写片段等价（同 schema），块按文件分开落盘。
- 引用索引与资产实际引用一致（改名影响清单可信）。
- 被引用 0 次的类型标为"无人读取"，保存前提示删除。

## 4. 依赖接口与验收

- 消费：目录结构定义、配置管线合并入口、地图与图资产扫描。
- 验收：四块各建一例往返无损；改名影响清单与全量扫描一致。

**相关文档**：[rel-01 UXD](../uxd/rel-01-catalog.md) · [rel-01 runtime spec](../spec-runtime/rel-01-catalog.md)
