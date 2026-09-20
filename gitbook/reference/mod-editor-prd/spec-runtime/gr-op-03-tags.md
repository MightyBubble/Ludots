# gr-op-03 runtime spec · 节点：标签

> 引擎实现任务书。第一性需求见 [gr-op-03 PRD](../prd/gr-op-03-tags.md)；现状见 [reference](../reference/gr-op-03-tags.md)。

## 1. 概述

标签读判定合同：单一 HasTag、有效语义、符号解析；已删节点的负空间也要成文。

## 2. 设计

- HasTag 判定保持"有效缓存一次读"：直接挂载与规则推导合并后的有效集，不现场跑规则。
- tag 名经注册表符号解析；未注册名编译期失败（与 tag-01 惰性注册的边界一致：图编译时名字必须已可注册）。
- **治理项 G8**：ADR #876 删除 SelectTagInMask/LookupTagDisplayToken 后，"纯读选 tag id"节点空档（状态栏 curState 场景无一等节点）。ADR 留活口可重立：新 op 输入绑通用 tag 集/用户表，禁绑专表；落地时同步清理表现层 TagDisplayTable 残名。与 TODO 总账 T8 同源。

## 3. 精确语义与不变量

- 有效集 = 直接挂载 ∪ 规则推导；HasTag 永不现场推导。
- 实体无 tag 组件 → 一切除法之外的默认假，不产生诊断。

## 4. 迁移与治理

G8 入 todo/graph.md；重立提案须附 ADR #876 活口引用与通用表绑定证明。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-03 PRD](../prd/gr-op-03-tags.md) · [reference](../reference/gr-op-03-tags.md)
