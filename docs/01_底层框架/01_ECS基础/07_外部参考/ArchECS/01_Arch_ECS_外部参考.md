---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - Arch ECS
状态: 草案
---

# Arch ECS 外部参考

# 1 项目概述
## 1.1 项目定位与要解决的问题

- Arch 是一个高性能、极简的 C# ECS，以 Archetype+Chunk 的连续内存布局为核心，面向游戏开发与数据导向编程。
- 设计目标强调缓存效率、迭代速度与分配性能，同时保持 API 最小化、可扩展。

## 1.2 版本与关键依赖

- 官方文档入口: `https://arch-ecs.gitbook.io/arch`
- 官方声明支持：.NETStandard 2.1 与 .NET 8（因此可用于 Unity/Godot/纯 C# 项目）。
- Ludots 口径：以仓库内 vendored 源码为准，不依赖外部 NuGet 的版本漂移。

# 2 架构与技术选型
## 2.1 总体架构与模块边界

- World：实体的容器与调度入口，承载创建/销毁/查询等核心 API。
- Archetype：同一组件结构的实体分组容器。
- Chunk：组件列式数据的连续存储块，面向线性遍历与缓存局部性。
- Query/QueryDescription：用“组件结构匹配”表达过滤与遍历意图。
- Utilities/Extensions：事件、EventBus、SourceGenerator、工具等可选扩展。

## 2.2 核心数据结构与关键流程

- 创建：`World.Create(...)` 创建世界与初始容量/Chunk 参数。
- 查询：通过 `QueryDescription` 组合过滤条件，World 侧提供 Query 缓存与多种枚举方式。
- 结构变更：组件增删/实体迁移属于“结构变更”，需要与遍历/并行执行边界对齐；推荐用 CommandBuffer 延迟并在安全阶段回放。

## 2.3 并发模型与生命周期

- 官方强调可通过 JobScheduler 并行执行 Query 或自定义 Job，并尽量避免垃圾生成。
- Ludots 口径：并行阶段只允许读或写“本实体组件列”，所有结构变更统一放到安全阶段集中执行。

## 2.4 技术选型与约束

- Arch 的性能假设是“开发者自律”：热路径减少背景检查与验证，避免隐藏成本；因此需要项目侧用规范与测试固化约束。
- Chunk 大小口径在官方不同页面存在不一致描述；Ludots 不在业务侧硬编码固定 chunk 字节数，统一以 vendored 源码与 World 配置为准。

# 3 核心能力矩阵
## 3.1 能力点与映射表

| 能力点 | Arch 支持情况 | Ludots 映射 | 风险与成本 |
|---|---|---|---|
| World 生命周期 | `World.Create/Dispose/TrimExcess` | `src/Libraries/Arch/src/Arch/Core/World.cs` | 需要明确 World 所属生命周期与清理责任 |
| Query 与过滤 | `QueryDescription.WithAll/WithAny/None/WithExclusive`，Query 缓存 | `src/Libraries/Arch/src/Arch/Core/World.cs` | 误用 WithExclusive 可能漏匹配；过多动态结构导致 archetype 碎片 |
| 批量操作 | World 提供 Bulk/Batch 能力入口 | `src/Libraries/Arch/src/Arch/Core/World.cs`（内部批量创建） | 大量结构抖动会带来迁移/碎片；需要波次化与集中阶段 |
| 结构变更延迟 | CommandBuffer 延迟并回放 | `src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs` | 回放阶段需要预算与失败策略；应复用 buffer 避免 GC |
| 结构变更标注 | `[StructuralChange]` 标注 API | `src/Libraries/Arch/src/Arch/Core/Utils/StructuralChangeAttribute.cs` | 需要配套“禁止遍历中变更”的约束与测试 |
| 事件钩子 | 通过 EVENTS 标志启用 World hooks | `src/Libraries/Arch/src/Arch/Core/Events/*` | 默认关闭；启用后回调可能成为隐式控制流，需要限制用途 |
| EventBus 扩展 | Arch.Extended 提供 EventBus | `src/Libraries/Arch.Extended/Arch.EventBus/*` | 需要明确“调试/桥接”与“玩法主干”边界 |
| Jobs/Scheduler | JobScheduler 并行 Query/Job | `src/Libraries/Arch/src/Arch/Core/Jobs/*` 与 `World.SharedJobScheduler` | 并行与结构变更必须分阶段；共享状态写入要隔离 |

# 4 适用性评估
## 4.1 适配成本与风险

- 适配成本低：Ludots 已 vendored Arch，并在多个系统里大量使用 Query、CommandBuffer、事件与 Job 相关能力。
- 核心风险集中在三类：
  - 组件设计不当导致 Chunk 飞线（托管引用/集合进入热组件）。
  - 结构变更时机不当导致遍历阶段崩溃或并行数据竞争。
  - 事件与总线被滥用，形成隐式依赖与不可审计链路。

## 4.2 与现有实现差异

- 与 Unity.Entities 类似：结构决定存储与遍历性能；不同点在于 Arch 更“裸”、更信任开发者，意味着更需要项目侧规范化与测试约束。

# 5 参考价值（必须明确优缺点）
## 5.1 值得吸收的点

- 以 Archetype+Chunk 保证连续内存与高效遍历，适合规模化单位与 0GC 热路径。
- QueryDescription 的结构过滤表达力强，易形成稳定、可审计的系统边界。
- CommandBuffer 将结构变更从遍历阶段剥离出来，天然适配并行阶段与安全点回放。
- 事件系统默认关闭，强调“只为用到的能力付费”，避免后台机制侵入热路径。

## 5.2 不适合/不建议引入的点

- 不建议把 World Events 当作玩法主干执行链路：回调是隐式控制流，难以治理与预算。
- 不建议在 Query 迭代中直接做结构变更：会破坏遍历安全与并行可预测性。

## 5.3 对 Ludots 的落地点（回填清单）

- 将以下内容固化到 ECS基础 使用手册，并作为项目口径真源：
  - Query 高效写法与 QueryDescription 复用策略。
  - CommandBuffer 与结构变更安全阶段切分（含并行阶段）。
  - JobScheduler 的 Schedule/Complete/Playback 三阶段边界。
  - 事件系统的用途边界与 Cleanup System 模式。
  - 内存飞线禁止条款与组件冷热拆分原则。

# 6 结论与建议
## 6.1 是否引入

- 结论：继续采用 Arch 作为 Ludots ECS 基础设施；不引入其他 ECS 作为运行时备用方案。

## 6.2 后续动作

- 回填并裁决：将 “使用手册/茶碟速查表” 升级为可执行的验收条款，并补齐 0GC 与结构变更边界的测试用例。

