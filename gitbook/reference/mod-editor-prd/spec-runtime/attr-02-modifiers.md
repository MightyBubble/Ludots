# attr-02 runtime spec · 修改器

> 引擎实现任务书。第一性需求见 [attr-02 PRD](../prd/attr-02-modifiers.md)；现状见 [reference](../reference/attr-02-modifiers.md)。

## 1. 概述

修改器数据形状与即时/聚合双路径的语义合同；写入权威单入口保持。

## 2. 设计

- 数据形状保持：三值 byte 枚举、定长三段数组+Count、容量常量（事实页）；即时路径逐条顺序执行+约束钳制，聚合路径绕过基线钳制、上限裁决归聚合管线（attr-03）。
- 写入权威保持五入口单点：快照、值不变早退、脏管道、表现位、异常回滚；不新增旁路。
- **治理项 A1**：修改器溢出改抛错，与 configParams 溢出同语义，错误带效果 id 与序号。
- **治理项 A2/A3**：clampToBase 下 GetBase 实返聚合上限（拆"读基线"与"读钳制上限"）；SetCurrentInternal 无条件置 DefinedMask（收敛为仅真实写入置位）。

## 3. 精确语义与不变量

- 运算序=声明序，Override 不终止后续条目；事务内即时修改器提交前对外不可见，提交原子回写；值不变早退不打脏、不发表现位。

## 4. 迁移与治理

现状即基线；A1-A3 见 todo/attribute.md。A1 随加载器错误统一批次落地；A2/A3 属读语义拆分，需先审计全部调用方。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[attr-02 PRD](../prd/attr-02-modifiers.md) · [attr-03 runtime spec](attr-03-aggregation.md)
