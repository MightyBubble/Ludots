# misc-02 runtime spec · 物品与兑换

> 引擎实现任务书。第一性需求见 [misc-02 PRD](../prd/misc-02-items-exchange.md)；现状见 [reference](../reference/misc-02-items-exchange.md)。

## 1. 概述

Items 三表（形状/布局/定义）与 Exchange 操作表的加载、运行与效果注入合同。

## 2. 设计

- 加载合同保持：四表 ArrayById；定义的 shape 必填且互查；maxStack ≤0 归一；Exchange 两段式 LoadIds 先收集 id 再对 Item/Relationship 注册表解析。
- 运行合同保持：InventoryRuntimeService 管容器实例；EquipmentGrantSync 同步装备效果/技能授予；ExchangeRuntime 把操作注入效果系统（投入校验→扣除→产出→关系门槛先置）。
- **治理项（D3）**：四张根表全空——内容全在 mod。与 T3 联动：确认目录条目有消费方；文档侧长期标注"根表占位"。
- **治理项**：rotatable 四旋转由加载期派生，无独立声明面——保持派生，不落表。

## 3. 精确语义与不变量

- 物品实例的占格 = 形状掩码 × 当前旋转 ∈ 4 旋转集。
- 兑换原子性：门槛不满足或投入不足时零变更。
- 引用解析失败 = 启动失败，无跳过。

## 4. 迁移与治理

现状即基线；D3 对账入 TODO（todo/domains.md）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[misc-02 PRD](../prd/misc-02-items-exchange.md) · [reference](../reference/misc-02-items-exchange.md)
