# gr-op-11 runtime spec · 节点：生命周期与内建

> 引擎实现任务书。第一性需求见 [gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md)；现状见 [reference](../reference/gr-op-11-lifecycle-builtin.md)。

## 1. 概述

生命周期图合同：事务开关、委托内建、组合门。

## 2. 设计

- BeginLifecycleTransaction 只做事务开启语义；InvokeBuiltin 一条指令委托到注册的 C# handler（DelegatedBuiltin），参数从效果上下文合并读取——图不重传业务参数。
- 效果组合编译对 Lifecycle 域 fail-closed：与 Relationship 域同一套 Unsupported 元数据机制。
- 内建注册表对 mod 扩展开放（加载窗口内注册，cfg-08），注册表冻结后新 handler 拒绝。
- **治理项**：事务外调用内建的校验目前依赖生命周期管线隐式状态——补一条编译期可达性检查（InvokeBuiltin 的事务前驱可达性）。

## 3. 精确语义与不变量

- 同一事务内的内建同生共死；链中断即回滚。
- handler 符号编译期解析；未注册即整图失败。
- 内建不产生图值线输出——副作用全部落在实体与效果上下文。

## 4. 迁移与治理

现状即基线；事务前驱可达性检查入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-11 PRD](../prd/gr-op-11-lifecycle-builtin.md) · [reference](../reference/gr-op-11-lifecycle-builtin.md)
