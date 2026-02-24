---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - Jobs 与 JobScheduler
状态: 草案
---

# Jobs 与 JobScheduler 摘录

- 来源:
  - https://arch-ecs.gitbook.io/arch/documentation/concepts
  - https://arch-ecs.gitbook.io/arch/documentation/world
- 说明: 本文为官方 Concepts/World 页面中与 Jobs/JobScheduler 相关内容的节选，非完整镜像。

## 1 官方定位（摘要）

- 为了在“大量数据与实体”场景进一步提效，Arch 提供多线程与 Job 能力。
- 官方强调：多线程与 Job 的设计目标之一是避免产生垃圾（不引入额外 GC）。
- Arch 提供自研的 JobScheduler，用于并行执行 Query 或自定义 Job。

## 2 World 与多线程入口（摘要）

- World 文档列出 Multithreading 能力：可访问 JobScheduler 并行运行 Query 或自定义 Job；并且 World 也可直接使用 Events、CommandBuffer 等能力。

## 3 注意事项（用于 Ludots 落地）

- 官方 Concepts 页面在“Chunk 大小”口径上与其他页面可能存在不一致描述；Ludots 的口径以 vendored 源码与运行时配置为准，不在业务侧硬编码 chunk 固定大小。
