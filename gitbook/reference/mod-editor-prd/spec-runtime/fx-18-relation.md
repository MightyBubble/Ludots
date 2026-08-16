# fx-17 runtime spec · 关系操作

> 引擎实现任务书。第一性需求见 [fx-17 PRD](../prd/fx-18-relation.md)；现状见 [reference](../reference/fx-18-relation.md)。

## 1. 概述
关系操作合同：三种操作、槽位与条件字段校验、事务边界与可执行性。

## 2. 设计
- SetParent 保持 GasTransactional：事务内 StageSetParent（含可选吸附），可随效果回滚。
- 运行期实体失效（subject/parent 不存活、父缺位置）抛错带实体 id 的错误合同保持。
- **治理项 E13**：RemoveParent/EnsureLink 注册为 Unsupported(Relationship)，计划编译 fail-closed——"能写出合法 JSON 却无法通过启动"。收口二选一：认证 Relationship 原子域（staged 化两操作），或 loader 前置拒绝并在错误中说明（todo/effect.md E13）。

## 3. 精确语义与不变量
- subject 永不为 None；SetParent/EnsureLink 的 parent 永不为 None（loader 保证）。
- snap 仅在 SetParent 且父有 WorldPositionCm 时生效；relationshipType id 仅 EnsureLink 携带非零值。
- 可执行操作集合 ⊆ 计划编译认证集合（消除"可配置不可执行"是 E13 验收标准）。

## 4. 迁移与治理
现状即基线；E13 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-17 PRD](../prd/fx-18-relation.md) · [reference](../reference/fx-18-relation.md)
