# gr-03 runtime spec · 六种 Kind

> 引擎实现任务书。第一性需求见 [gr-03 PRD](../prd/gr-03-kinds.md)；现状见 [reference](../reference/gr-03-kinds.md)。

## 1. 概述

kind 策略合同：返回槽映射、节点白名单、监听相容、预设寄存器保护。

## 2. 设计

- 白名单判定保持四条规则：ScriptOnly 节点仅 Script；Effect 图全放行；Script 图仅 Pure；其余 kind 需 Pure，唯一例外 Derived 叠加 DerivedAttributeWrite（WriteSelfAttribute）。
- 程序校验四件：RequireAllowed、寄存器边界、分支目标、必含 HaltReturnInt；策略错误码保持八值封闭。
- 监听相容保持三条：InvokeBuiltin 拒；需监听宿主上下文的 LoadConfig* 拒；纯相位须 Pure、非纯相位须 Pure+GasTransactional。
- 返回槽映射固定：Script→I[0]（HaltReturnInt 写 I[A] 入 ReturnInt）、Score→F[0]、Validation→B[0] 且执行前清零、Query→TargetList+schema、Derived 直写自身属性；E0/E1/E2 与宿主 ABI 槽编译期 Reserve + scratch 保护（E2 三种来源保持枚举）。

## 3. 精确语义与不变量

- kind 注册后不可变；挂接点终检必过 kind 匹配；保留槽任何图不可写用；Derived 对自身属性的写只经 WriteSelfAttribute。

## 4. 迁移与治理

现状即基线；返回槽与 I[0] 环境约定的成文化归 gr-04（G3）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-03 PRD](../prd/gr-03-kinds.md) · [reference](../reference/gr-03-kinds.md) · [gr-04 spec](gr-04-compilation.md)
