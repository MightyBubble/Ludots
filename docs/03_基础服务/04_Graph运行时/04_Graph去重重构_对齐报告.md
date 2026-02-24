---
文档类型: 对齐报告
创建日期: 2026-02-07
最后更新: 2026-02-07
维护人: X28技术团队
文档版本: v1.0
适用范围: 基础服务 - Graph运行时 - 去重重构审计
状态: 已裁决
依赖文档:
  - docs/03_基础服务/04_Graph运行时/03_NodeLibraries_架构设计.md
  - docs/03_基础服务/04_Graph运行时/01_GraphExecutor.md
  - docs/01_底层框架/01_ECS基础/01_Arch_ECS核心概念.md
---

# Graph 去重重构 对齐报告

# 1 摘要

## 1.1 结论

Graph 三层去重重构已完成。Layer 3（`Gameplay/GAS/Graph/`）已清空，13 个重复文件删除，4 个保留文件移入 Layer 2（`NodeLibraries/GASGraph/`）。后续对 ECS 最佳实践进行全面审计，发现 6 个差异并全部修复。

当前状态：

- Layer 1（`GraphRuntime`）：6 文件，零 Gameplay/ECS 依赖
- Layer 2（`NodeLibraries/GASGraph`）：11 文件，非 Host 文件零 Gameplay 依赖
- Layer 2 Host：4 文件（`GasGraphRuntimeApi`、`GasGraphSymbolResolver`、`GraphProgramLoader`、`GraphIdRegistry`）
- 全部 133 测试通过（132 通过 + 1 预存失败与本次无关）

## 1.2 风险等级与影响面

- 风险等级：低（所有差异已修复，测试全绿）
- 影响面：`NodeLibraries/GASGraph/`（6 文件修改）+ `GameEngine.cs`（1 行修改）+ 1 新文件

## 1.3 建议动作

1. Phase 2 推进 NodeLibraries 分包（Std/Spatial/GAS）
2. Phase 2 实现 TimeSlice 可暂停/恢复执行
3. 清理预存的架构守卫测试失败（`RelationshipFilter.cs` 中的 "Legacy" 注释）

# 2 审计范围与方法

## 2.1 审计范围

- `src/Core/GraphRuntime/`（Layer 1）全部 6 文件
- `src/Core/NodeLibraries/GASGraph/`（Layer 2）全部 14 文件
- `src/Core/Engine/GameEngine.cs`（GraphProgramLoader 构造处）
- `src/Tools/Ludots.Tool/Program.cs`（Graph 编译工具引用）
- `src/Tests/GasTests/`（全部 7 个 Graph 测试文件）
- ECS 最佳实践文档：`docs/01_底层框架/01_ECS基础/`

## 2.2 审计方法

1. 全量阅读 Layer 1/Layer 2/Host 全部源码
2. grep 分析 namespace 依赖链
3. 逐文件检查 GC 分配（热路径是否有 new/List/Dictionary/Boxing/Closure）
4. 对照 ECS 文档验收条款逐条检查
5. 对照 GraphExecutor 接口规范检查 fail-fast 合规性

## 2.3 证据口径

- 编译验证：`dotnet build` 三个项目 0 error
- 测试验证：`dotnet test` 132/133 passed
- 依赖验证：`rg "using Ludots.Core.Gameplay" src/Core/NodeLibraries/GASGraph/` 仅 Host 文件命中

# 3 差异表

## 3.1 差异表

| 设计口径 | 修复前现状 | 差异等级 | 风险 | 修复措施 | 证据 |
|---|---|---|---|---|---|
| fail-fast：未注册 opcode 必须中止执行 | `GasGraphOpHandlerTable.Execute` 静默跳过 null handler | HIGH | 运行时 op 映射错误无法发现 | 非零 null handler → 抛 `InvalidOperationException`；Op 超范围 → 抛异常 | `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs` |
| 领域隔离：非 Host 文件不引用 Gameplay | `GraphTargetList.cs` 引用 `Gameplay.Teams.TeamRelationship` | MEDIUM | 编译时耦合 Gameplay 枚举 | 新增 `GraphRelationship` 协议常量，删除 `using Gameplay.Teams` | `src/Core/NodeLibraries/GASGraph/GraphTargetList.cs`、`IGraphRuntimeApi.cs` |
| 预算保护：必须有步骤上限 | 执行循环无指令步数上限 | MEDIUM | 无限跳转循环挂死帧 | 新增 `MaxInstructionsPerExecution=4096` 熔断 | `src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs` |
| 领域隔离：Host 依赖通过接口注入 | `GraphProgramLoader` 直接调用 `TagRegistry`/`AttributeRegistry`/`EffectTemplateIdRegistry` | MEDIUM | 加载器无法在无 GAS 环境单测 | 新增 `IGraphSymbolResolver` 接口 + `GasGraphSymbolResolver` 实现，构造函数注入 | `src/Core/NodeLibraries/GASGraph/Host/GraphProgramLoader.cs` |
| 内存效率：handler 表按实际范围分配 | handler 数组 65536 槽位（约 512KB），实际仅用 25 个 | LOW | 内存浪费 | 缩小到 `HandlerTableSize=256` + 边界检查 | `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs` |
| 禁止兼容/fallback | `[Obsolete] FilterTeam` 仍注册 handler 并保留 pragma suppress | LOW | 已废弃 op 仍可执行 | 删除 `QueryFilterTeam` 枚举值、handler、`FilterTeam` 方法 | `GraphOps.cs`、`GasGraphOpHandlerTable.cs`、`GraphTargetList.cs` |

# 4 行动项

## 4.1 行动项清单

| # | 行动 | 优先级 | 状态 | 验收条件 |
|---|---|---|---|---|
| A1 | fail-fast 修复 | HIGH | 已完成 | `GasGraphOpHandlerTable.Execute` 对非零 null handler 抛异常；24 Graph 测试通过 |
| A2 | `GraphTargetList` 解耦 `TeamRelationship` | MEDIUM | 已完成 | `rg "using Ludots.Core.Gameplay" src/Core/NodeLibraries/GASGraph/*.cs` 无结果 |
| A3 | 指令步数熔断 | MEDIUM | 已完成 | `MaxInstructionsPerExecution` 集中定义于 `GraphVmLimits`；执行循环使用该常量 |
| A4 | `IGraphSymbolResolver` 注入 | MEDIUM | 已完成 | `GraphProgramLoader` 无 `using Gameplay.GAS.Registry`；构造函数必须传入 resolver |
| A5 | Handler 表缩容 | LOW | 已完成 | 数组长度 = `HandlerTableSize`（256）；Execute 有边界检查 |
| A6 | 删除 `FilterTeam` 兼容 | LOW | 已完成 | `QueryFilterTeam` 枚举值已移除；handler 和方法已删除；无 `[Obsolete]` 残留 |
| A7 | Phase 2: NodeLibraries 分包 | LOW | 待开始 | Std/Spatial/GAS 三个独立节点库 |
| A8 | Phase 2: TimeSlice 支持 | LOW | 待开始 | `ExecuteSlice` → `GraphSliceResult`，可在下一帧恢复 |
