# ent-01 editor spec · 实体模板

> 编辑器实现任务书。编辑器需求见 [ent-01 UXD](../uxd/ent-01-templates.md)；引擎侧见 [runtime spec](../spec-runtime/ent-01-templates.md)。

## 1. 概述

实体编辑器实现：组件 schema 表单、分片写回、覆盖对照。

## 2. 设计

- **组件 schema 投影**：消费引擎组件注册表，逐组件生成字段表单元数据（字段/类型/默认）——表单不自造字段。
- **分片写回**：模板条目写本 mod 分片（一模板一文件的分片目录）。
- **覆盖对照**：字段级深合并视图（基线→生效→覆盖来源），消费合并预览数据源（cfg-05 editor spec）。

## 3. 精确语义与不变量

- 表单可写的字段集 = 组件解析器接受的字段集，同源。
- 写回分片必须通过模板加载器校验。

## 4. 依赖接口与验收

- 消费：组件注册表与解析 schema、模板表、合并预览数据源。
- 验收：表单改值→保存→启动后实例值一致；schema 外字段无法产生。

**相关文档**：[ent-01 UXD](../uxd/ent-01-templates.md) · [ent-01 runtime spec](../spec-runtime/ent-01-templates.md)
