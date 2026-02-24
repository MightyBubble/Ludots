---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - CommandBuffer
状态: 草案
---

# CommandBuffer 摘录

- 来源:
  - https://arch-ecs.gitbook.io/arch/documentation/concepts
  - https://arch-ecs.gitbook.io/arch/documentation/world
- 说明: 本文为官方 Concepts/World 页面中与 CommandBuffer 相关内容的节选，非完整镜像。

## 1 官方定位（摘要）

- 官方说明：当你不希望立即对实体做结构变更时，可以使用 CommandBuffer 延迟这些变更，并在之后某个时间点统一执行。
## 2 World 能力入口（摘要）

- World 文档提到：可以直接使用 CommandBuffer 等能力（与 Events、JobScheduler 并列）。
## 3 对 Ludots 的提示
- CommandBuffer 的核心价值是把“遍历阶段”与“结构变更阶段”解耦，避免在 Query 迭代中直接 Add/Remove/Create/Destroy。
- Ludots 侧最佳实践与模板代码在 `07_使用手册/02_CommandBuffer与结构变更.md` 中固化；外部参考仅保留吸收点与回填清单。
