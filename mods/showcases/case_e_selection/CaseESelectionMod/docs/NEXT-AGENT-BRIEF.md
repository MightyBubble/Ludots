# 下一位：图当可调用函数 — 先写方案

> **正本**：[可调用函数远景](../../../../../gitbook/architecture/graph-callable-function-vision.md)  
> **进度入口**：[图能力状态](../../../../../gitbook/architecture/graph-capability-status.md)  
> **Case E 说明**：[结构与配置](./case-e-config-structure.html)

## 这张单子要你干什么

写一份可评审的方案（Markdown），交人审。不要在本单里合入 Core 大改。

Case E 框选预览（PR **#1444**）已经做到：`DispatchCollectionEvent` 可以出现在非 Query 的连续图里，命中结果由图自己写集合，落定 / 点选用 `InvokeGraph` 复用同一张命中图。接下来要定的是：产品上「图 = 可调用函数」长什么样，以及和现有 `Query` / `Script` / `TriggerGraph` / `continuousQuery` 怎么统一。

## 必读（按顺序）

1. 正本全文（含方案模板与验收表）  
2. 本目录 `case-e-config-structure.html` 第 10 节  
3. `gitbook/architecture/graph-capability-status.md` 与图 Kind / 宿主表  
4. `InteractionContextProfileRegistry`（`continuousQuery` 与 `DispatchCollectionEvent`）  
5. `GasGraphOpHandlerTable` / `GraphProgramRegistry` 对 `InvokeGraph` 的目标校验  
6. issue **#1084**、**#1099**（Query 准纯净）——方案里必须写清与「可写业务函数」怎么并存

## 方案必须写清

| 问题 | 说明 |
|------|------|
| S1 还是 S2 | 图内副作用，还是纯返回 + 声明式写入；禁止静默代写 |
| 字段改名 | `continuousQuery` 产品名是否改、何时改、兼容策略 |
| Invoke 纯度 | 带副作用的业务函数 vs 纯 FuncLib；目录与命名 |
| 与 #1084 | 禁止互相否定却不写迁移 |
| 禁区 | 见正本「边界」 |

## 交付检查

- [ ] 填满正本「方案怎么交」各节  
- [ ] 验收表每条都有设计对应  
- [ ] 写明本单不实现的项  
- [ ] 列出要改的文件 / 测试  
- [ ] 与 Case E 三集合（`case_e.selectable` / `case_e.box_hover` / `selected`）对齐说明  

## 本切片已落地（对照用，勿当远景已完成）

- 连续框选：非 Query + `DispatchCollectionEvent` → `case_e.box_hover`  
- 离框：按程序里的集合键清悬停  
- 落定 / 点选：`InvokeGraph` → `graph.case_e.box_hit` → 写 `selected`  
- 热路径：`DispatchCollectionEvent` 不 `new Entity[]`；合并写集走 span  

细节与验收句以正本和 HTML 为准。
