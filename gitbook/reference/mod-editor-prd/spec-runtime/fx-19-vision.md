# fx-23 runtime spec · 视野揭示

> 引擎实现任务书。第一性需求见 [fx-22 PRD](../prd/fx-19-vision.md)；现状见 [reference](../reference/fx-19-vision.md)。

## 1. 概述
知识区域揭示合同：范围/层/记忆/强度参数、周期刷新与移除衰减、可执行性。

## 2. 设计
- KnowledgeAreaRevealRuntime 的 Reveal/DecayArea 合同保持：viewer + 源 + 中心 + 描述符 + 当前步；层集合 1..MaxLayers；揭示中心解析失败的"跳过不抛"语义保持。
- **治理项 E14**：HandleRevealArea/HandleDecayRevealArea 实现完整但注册为 Unsupported(Vision) 且全库无调用点——即使 InvokeBuiltin 也被计划编译 fail-closed。收口二选一：认证 Vision 原子域并接通调用链，或 loader 前置拒绝 revealArea 块并删除不可达配置面（todo/effect.md E14）。

## 3. 精确语义与不变量
- 揭示写入以 viewer 为归属的知识区域；记忆衰减按描述符 TTL 由视野运行时推进。
- After 生命周期的周期刷新只重新揭示，不叠加记忆（TTL 从最后一次揭示起算）。
- 可执行操作集合 ⊆ 计划编译认证集合（E14 验收：不再存在"字段全可写但永不可执行"的块）。

## 4. 迁移与治理
现状即基线；E14 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-22 PRD](../prd/fx-19-vision.md) · [reference](../reference/fx-19-vision.md)
