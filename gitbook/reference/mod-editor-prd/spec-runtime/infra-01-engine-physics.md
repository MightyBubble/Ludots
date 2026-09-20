# infra-01 runtime spec · 引擎与物理配置

> 引擎实现任务书。第一性需求见 [infra-01 PRD](../prd/infra-01-engine-physics.md)；现状见 [reference](../reference/infra-01-engine-physics.md)。

## 1. 概述

引擎固定时钟与 Physics2D 三件（时钟/求解器/运动学）的加载、校验与消费合同。

## 2. 设计

- 四文件 DeepObject 合同保持：mod 局部覆盖、缺省补齐、逐字段区间校验。
- 运动学"必显式"合同保持：三字段缺失即抛错（无默认注入），层白名单校验对层注册表。
- **治理项（D2）**：引擎时钟代码缺省（50）与实配（20）不一致、物理时钟代码缺省（15）与实配（60）不一致——排障误导。方向：把缺省值收敛为与事实页同源的单一出处，或文档化"缺省仅是兜底"；短期在错误信息与日志里同时打印实配与缺省。
- **治理项**：solver 的材料默认（摩擦/弹性/阻尼）与 kinematic 容量分居两文件但同域——评估目录侧合并认领（同 T3 对账问题）。

## 3. 精确语义与不变量

- 物理补步数 ≤ MaxStepsPerFixedTick；超限丢步不崩。
- 修正比 ∈ [0,1]；容量 ≥ 1；宽相 CellSizeCm ≥ 1。
- 接触事件只对白名单层发射；队列溢出报错不清默。

## 4. 迁移与治理

现状即基线；D2 缺省收敛入 TODO（todo/domains.md）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[infra-01 PRD](../prd/infra-01-engine-physics.md) · [reference](../reference/infra-01-engine-physics.md)
