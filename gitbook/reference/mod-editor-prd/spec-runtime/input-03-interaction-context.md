# input-03 runtime spec · 交互上下文档案

> 引擎实现任务书。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；现状见 [reference](../reference/input-03-interaction-context.md)。

## 1. 概述
上下文合同：五拼装位档案、exec 期自动挂载/回收、挂载上下文意图优先。

## 2. 设计
- 生命周期保持：声明档案的 exec 开始把档案挂载为 `InteractionContextInstance`（落在 exec 载体的控制域 representative，每域最新激活胜出），结束在下一次系统更新回收；同实体去重跟踪。
- 解析链保持：仲裁器读挂载上下文意图，优先于玩家默认（input-01 同链）；无挂载即 steady state。
- **前移校验**：能力引用的档案名当前在执行开始才报错——把存在性检查前移到能力加载期（档案安装序已满足），失败更早更准。

## 3. 精确语义与不变量
- 挂载上下文的存活域 = 对应 exec 实例的存活域；异常 exec 不泄漏挂载。
- 档案五键全部可选；空档案 id 合法。

## 4. 迁移与治理
交互上下文栈已退役（#1306 路线④，路线记录见 [reference](../reference/input-03-interaction-context.md) §3）；现状即基线，不改数据格式。

## 变更记录
- v2（2026-08-29）：生命周期实体化措辞（栈退役）。
- v1（2026-08-15）：初版。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [reference](../reference/input-03-interaction-context.md)
