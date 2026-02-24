---
文档类型: 接口规范
创建日期: 2026-02-05
最后更新: 2026-02-07
维护人: X28技术团队
文档版本: v0.3
适用范围: 基础服务 - Graph运行时 - GraphExecutor
状态: 已实现
---

# GraphExecutor 接口规范

# 1 概述

GraphExecutor 负责在确定性约束下执行 GraphProgram。本文给出最小口径与约束，避免上层系统把 Graph 当作"无限递归/无限步数"的脚本引擎。

# 2 核心接口（概念形状）

## 2.1 Execute/Step

约束口径：

- 执行必须是确定性的：相同输入与相同初始状态得到相同输出。
- 必须可 fail-fast：非法指令/越界访问/未注册 opcode 必须抛错，不允许静默跳过。
- 必须有步骤上限：防止无限跳转循环挂死执行帧。

## 2.2 两个执行入口

**Layer 1 通用执行器**（`GraphRuntime.GraphExecutor`）：

```csharp
public static void Execute<TState>(ref TState state, ReadOnlySpan<GraphInstruction> program, IOpHandlerTable<TState> handlers)
```

- 泛型设计，适用于任何 state 类型。
- 未注册 opcode（非零）抛 `InvalidOperationException`。
- Op == 0 静默 continue。

**Layer 2 GAS 执行入口**（`GasGraphOpHandlerTable.Execute`）：

```csharp
public static void Execute(ref GraphExecutionState state, ReadOnlySpan<GraphInstruction> program, GasGraphOpHandlerTable handlers)
```

- 使用 concrete ref struct 而非泛型（.NET 8 不支持 `allows ref struct` 泛型约束）。
- 行为与 Layer 1 一致：未注册 opcode → fail-fast。
- 增加指令步数熔断：超过 `GraphVmLimits.MaxInstructionsPerExecution`（4096）抛异常。
- 增加 opcode 边界检查：超出 `GraphVmLimits.HandlerTableSize`（256）抛异常。

## 2.3 fail-fast 行为矩阵

| 条件 | 行为 |
|---|---|
| Op == 0 | 静默 continue |
| Op > 0 且 Op < HandlerTableSize 且 handler == null | 抛 `InvalidOperationException` |
| Op ≥ HandlerTableSize | 抛 `InvalidOperationException` |
| 步数 > MaxInstructionsPerExecution | 抛 `InvalidOperationException` |
| 目标实体 `!World.IsAlive` | 静默跳过（不抛异常） |

# 3 使用约束

- 稳定序：指令执行顺序固定，不允许依赖容器遍历顺序。
- 预算：`MaxInstructionsPerExecution = 4096` 为硬上限，集中定义在 `GraphVmLimits`。
- 寄存器文件：全部 stackalloc，执行结束自动释放。
- 事务边界：当前无 TimeSlice 支持（Phase 2），每次 Execute 必须完整执行或因异常终止。

# 4 预算常量（SSOT）

所有 VM 硬限制集中在 `GraphVmLimits`：

| 常量 | 值 | 用途 |
|---|---|---|
| `MaxFloatRegisters` | 32 | 浮点寄存器数量 |
| `MaxIntRegisters` | 32 | 整型寄存器数量 |
| `MaxBoolRegisters` | 32 | 布尔寄存器数量 |
| `MaxEntityRegisters` | 32 | 实体寄存器数量 |
| `MaxTargets` | 256 | 目标列表容量 |
| `MaxInstructionsPerExecution` | 4096 | 单次执行指令上限 |
| `HandlerTableSize` | 256 | opcode handler 表容量 |

# 5 代码入口（文件路径）

- Layer 1 通用执行器：`src/Core/GraphRuntime/GraphExecutor.cs`
- Layer 1 指令格式：`src/Core/GraphRuntime/GraphInstruction.cs`
- Layer 2 GAS 执行入口：`src/Core/NodeLibraries/GASGraph/GraphExecutor.cs`
- Layer 2 Handler 表：`src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs`
- 预算常量：`src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs`
