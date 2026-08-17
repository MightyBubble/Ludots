# ab-01 runtime spec · 技能定义骨架

> 引擎实现任务书。第一性需求见 [ab-01 PRD](../prd/ab-01-definition.md)；现状见 [reference](../reference/ab-01-definition.md)。

## 1. 概述

技能表加载与编译合同：排序注册、逐条全量校验、错误聚合、旧字段专门报错。

## 2. 设计

- 加载保持：默认根 + 分片，ArrayById 按 id 收集，**按 id 排序后注册**（跨 mod 覆盖与加载顺序解耦）；单条失败不中断扫描，末尾聚合一次抛出。
- 旧字段拦截保持逐类专门报错（indicator / onActivateEffects / 瞄准表现族 / 四项改名 / clockId "Turn"），错误文案含替代写法。
- presentation token 校验与文案目录同源：无 token 直译不校验；有 token 须已注册、有模板、非兜底键。

## 3. 精确语义与不变量

- exec 缺失即条目失败；items 允许为空；cooldown、toggleSpec、targeting 必填件与引用许可序（模板/图/属性/进度需求先于技能）保持。

## 4. 迁移与治理
现状即基线；无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-01 PRD](../prd/ab-01-definition.md) · [reference](../reference/ab-01-definition.md)
