# attr-01 runtime spec · 属性定义与约束

> 引擎实现任务书。第一性需求见 [attr-01 PRD](../prd/attr-01-definition.md)；现状见 [reference](../reference/attr-01-definition.md)。

## 1. 概述

属性注册与约束合同：首现注册、Ordinal、启动尾冻结、约束热替换边界。

## 2. 设计

- 注册表参数保持：id 从 0 连续、InvalidId=-1、Ordinal、冻结后幂等返回/新名抛错。
- 热替换三限制保持；唯一消费方为工作台热管线。
- **治理项**：扩展属性区（10001-20000）三件套为死链路（无生产者、id 进不了 64 槽缓冲）——接通或移除；`MAX_EXTENSION_ATTRS=1000` 死常量一并清理。
- 约束加载的字符串数字宽容收敛为严格数字，错误带条目上下文。

## 3. 精确语义与不变量

- 冻结点 = 全部配置加载器完成之后。

## 4. 迁移与治理

现状即基线；死链路处置入 TODO（T16）。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[attr-01 PRD](../prd/attr-01-definition.md) · [reference](../reference/attr-01-definition.md)
