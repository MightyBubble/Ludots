# fx-18 editor spec · 关系操作

> 编辑器实现任务书。编辑器需求见 [fx-18 UXD](../uxd/fx-18-relation.md)；引擎侧见 [runtime spec](../spec-runtime/fx-18-relation.md)。

## 1. 概述

Relation 效果表单的关系子表单：操作联动、槽位约束、可执行性警示。

## 2. 设计

- **操作联动**：operation 决定 snap/relationshipType 的可见与落盘，规则与 loader 同源。
- **槽位约束**：subject 选项集不含 None；parent 按 operation 收紧。
- **可执行性警示**：编辑器消费计划编译器的认证集合（同源判定），对现状不可执行操作出示警示而非硬拦（保存的是合法 schema，拦截在启动侧）。

## 3. 精确语义与不变量

- 表单落盘字段集与所选操作合法集一致；往返保存无损。
- 警示判定与启动期 fail-closed 判定同源，不手抄操作白名单。

## 4. 依赖接口与验收

- 消费：关系类型注册表、效果计划编译器的操作认证结果、保存管线。
- 验收：SetParent 下链型字段不落盘；选 RemoveParent 出现警示条且文案与启动错误同源。

**相关文档**：[fx-18 UXD](../uxd/fx-18-relation.md) · [fx-18 runtime spec](../spec-runtime/fx-18-relation.md)
