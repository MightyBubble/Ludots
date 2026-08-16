# fx-19 runtime spec · 兑换

> 引擎实现任务书。第一性需求见 [fx-19 PRD](../prd/fx-20-exchange.md)；现状见 [reference](../reference/fx-20-exchange.md)。

## 1. 概述
兑换执行合同：上下文组装、一次原子结算、失败记录不抛。

## 2. 设计
- HandleExecuteExchange：从合并参数读 `_ep.exchangeOperationId`（缺失或非正抛错）；`_ep.exchangeScopeKey` 有值走命名作用域。
- ExchangeExecutionContext(source, target, targetContext, scope) → ExchangeRuntime.TryExecute 一次结算；结果 RecordExchangeResult 进预算/诊断，失败 return 不抛。
- **治理项 E13（兑换分支）**：处理器注册为 Unsupported(Exchange)，计划编译 fail-closed。收口二选一：认证 Exchange 原子域（TryExecute 已是自足原子结算，补 staged 边界即可），或 loader 前置拒绝（todo/effect.md E13）。

## 3. 精确语义与不变量
- 一次效果执行至多一次兑换结算；成败整体原子，无半扣半给。
- 业务失败不是错误：不抛、不回滚效果、只记录。
- 操作 id 在加载期锁定，运行期不按名解析。

## 4. 迁移与治理
现状即基线；E13 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-19 PRD](../prd/fx-20-exchange.md) · [reference](../reference/fx-20-exchange.md)
