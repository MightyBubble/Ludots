# input-03 runtime spec · 交互上下文档案

> 引擎实现任务书。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；现状见 [reference](../reference/input-03-interaction-context.md)。

## 1. 概述
上下文合同：五拼装位档案、exec 期自动压栈/回收、栈顶意图优先。

## 2. 设计
- 生命周期保持：声明档案的 exec 开始压帧、结束按上下文实体回收；同实体去重跟踪。
- 解析链保持：仲裁器读栈顶帧意图，优先于控制方案默认（input-01 同链）。
- **前移校验**：能力引用的档案名当前在执行开始才报错——把存在性检查前移到能力加载期（档案安装序已满足），失败更早更准。

## 3. 精确语义与不变量
- 帧的存活域 = 对应 exec 实例的存活域；栈不因异常 exec 泄漏帧。
- 档案五键全部可选；空档案 id 合法。

## 4. 迁移与治理
现状即基线；加载期前移校验为小步引擎任务，不改数据格式。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [reference](../reference/input-03-interaction-context.md)
