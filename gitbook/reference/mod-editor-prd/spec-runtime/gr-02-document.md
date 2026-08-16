# gr-01 runtime spec · 图文档格式

> 引擎实现任务书。第一性需求见 [gr-01 PRD](../prd/gr-02-document.md)；现状见 [reference](../reference/gr-02-document.md)。

## 1. 概述

文档 schema 与创作门合同：顶层七字段、节点字段全表、端口常量集、FrontDoor 强制项。

## 2. 设计

- FrontDoor 保持四强制：kind 必填且该 kind 允许控制流创作；节点 next 硬拒；controlEdges/valueEdges 双键强制；id 补全大小写不敏感。
- 节点字段 schema 保持八族封闭（身份/常量/图符号/数据符号/关系/查询/形状/寄存器）；`graphId` 与 `functionName` 互斥由 schema 层拒绝。
- 端口常量集保持封闭（含 `case:` 前缀动态端口）；新增端口先扩常量集再开节点。

## 3. 精确语义与不变量

- 缺任一边键即格式错误；未知节点字段、未知端口在门或编译期拒绝，无静默忽略；id 补全规则稳定。

## 4. 迁移与治理

现状即基线（next 拒绝已落地，issue #861）；schema 只随引擎同源表演进。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-01 PRD](../prd/gr-02-document.md) · [reference](../reference/gr-02-document.md) · [gr-03 spec](gr-04-compilation.md)
