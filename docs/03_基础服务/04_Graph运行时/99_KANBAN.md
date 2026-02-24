---
文档类型: 开发看板
创建日期: 2026-02-05
最后更新: 2026-02-07
维护人: X28技术团队
文档版本: v0.2
适用范围: 03_基础服务 - 04_Graph运行时 - 开发看板
状态: 进行中
---

# Graph运行时 KANBAN

# 1 定位
## 1.1 本看板的唯一真相范围
- 本文档是本子系统当前开发计划唯一真相。
- 其它文档只允许引用卡片 ID。

## 1.2 不在本看板维护的内容
- 需求口径、架构设计、接口规范、配置结构与裁决条款。

# 2 使用规则
## 2.1 卡片字段规范
- ID：子系统内唯一，格式为 `K-001` 递增。
- 卡片：一句话描述交付结果。
- 负责人：明确到人。
- 验收标准：可判定，给出验证方法。
- 证据入口：仓库相对路径，指向代码或测试或对齐报告。

## 2.2 状态流转规范
- Backlog -> Doing -> Review -> Done

## 2.3 WIP 上限与阻塞处理
- Doing 列最多 5 张卡片。

# 3 看板
## 3.1 Backlog
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-007 | NodeLibraries 分包：Std/Spatial/GAS 三个独立节点库 | 待定 | 三个独立目录各自可独立编译测试 | `src/Core/NodeLibraries/` |
| K-008 | TimeSlice 支持：ExecuteSlice 可暂停/恢复 | 待定 | `ExecuteSlice` → `GraphSliceResult`；不同 slice 切分下输出一致 | `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs` |
| K-009 | ISymbolResolver 通用化：operand kind 机制 | 待定 | Core 只提供 symbol table + operand kind 机制，GASNodes 提供具体 resolver | `src/Core/NodeLibraries/GASGraph/Host/GraphProgramLoader.cs` |

## 3.2 Doing
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.3 Review
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|

## 3.4 Done
| ID | 卡片 | 负责人 | 验收标准 | 证据入口 |
|---|---|---|---|---|
| K-001 | Layer 3 去重：删除 13 个重复文件，保留文件移入 Layer 2 | X28 | `Gameplay/GAS/Graph/` 不存在；0 stale namespace 引用 | `src/Core/NodeLibraries/GASGraph/` |
| K-002 | fail-fast：未注册 opcode 抛异常 | X28 | `GasGraphOpHandlerTable.Execute` 对非零 null handler 抛异常 | `src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs` |
| K-003 | 指令步数熔断：MaxInstructionsPerExecution | X28 | 超 4096 步抛异常；常量在 `GraphVmLimits` | `src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs` |
| K-004 | GraphTargetList 解耦 TeamRelationship | X28 | 非 Host 文件零 `Gameplay.*` 引用 | `src/Core/NodeLibraries/GASGraph/GraphTargetList.cs` |
| K-005 | IGraphSymbolResolver 注入 | X28 | `GraphProgramLoader` 无 `Gameplay.GAS.Registry` 引用 | `src/Core/NodeLibraries/GASGraph/Host/GraphProgramLoader.cs` |
| K-006 | 删除 [Obsolete] FilterTeam 兼容 | X28 | `QueryFilterTeam` 枚举值、handler、方法全删 | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` |

# 4 里程碑
## 4.1 当前里程碑
- M1：Layer 3 去重 + ECS 对齐审计修复（已完成）

## 4.2 下一个里程碑
- M2：NodeLibraries 分包 + TimeSlice（K-007 ~ K-009）

# 5 变更记录
| 日期 | 变更人 | 变更摘要 |
|---|---|---|
| 2026-02-07 | X28 | 初始化看板；录入 K-001~K-006（已完成）+ K-007~K-009（Backlog） |
