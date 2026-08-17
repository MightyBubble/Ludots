# ab-06 runtime spec · 槽位系统

> 引擎实现任务书。第一性需求见 [ab-06 PRD](../prd/ab-06-slots.md)；现状见 [reference](../reference/ab-06-slots.md)。

## 1. 概述

槽位合同：8 槽四层缓冲、短路解析序、按来源回收、模板层上限校验。

## 2. 设计

- 四层缓冲保持：底座（定容 8）、临时授予（来源 tag 记账 + 按来源批量回收）、物品（装备同步整层重建）、形态（每帧 ClearAll 重算）。
- 解析保持：granted > itemGranted > form > base 逐层 HasOverride 短路；槽号越界 false；TryFindAbility 全槽扫；模板层 abilityIds ≤8 启动校验、未注册名启动失败。
- **治理项 AB8**：临时授予层无生产写入口（组件与回收 API 完备、仅测试使用）——接通效果授予技能的落地件或标注预留层。

## 3. 精确语义与不变量

- 解析序全局唯一：输入映射、面板、AI、订单走同一解析器；形态层帧间不持久。

## 4. 迁移与治理
现状即基线；AB8 立项后回写。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-06 PRD](../prd/ab-06-slots.md) · [reference](../reference/ab-06-slots.md)
