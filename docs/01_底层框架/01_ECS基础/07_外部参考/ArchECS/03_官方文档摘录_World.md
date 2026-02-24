---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - World
状态: 草案
---

# World 摘录

- 来源: https://arch-ecs.gitbook.io/arch/documentation/world
- 说明: 本文为官方 World 页面节选，非完整镜像。

## 1 World 的职责（摘要）

- World 存储全部实体，提供创建、销毁、查询等方法，并承载内部机制。
- 可同时存在多个 World，彼此完全隔离。

## 2 生命周期与常用 API（节选）

```csharp
var world = World.Create();
world.TrimExcess();
world.Dispose();
World.Destroy(world);
```

## 3 World 创建参数（节选）

```csharp
var customizedWorld = World.Create(
    chunkSizeInBytes: 16_382,
    minimumAmountOfEntitiesPerChunk: 100,
    archetypeCapacity: 8,
    entityCapacity: 64
);
```

## 4 关键说明（摘要）

- 上述参数是初始值指引，运行时可能按需要向上扩展。
- Arch 支持 “PURE_ECS” 取向：可直接围绕 World/Archetype/Chunk/Entity 工作，不必依赖更高层封装。
- World 相关章节会覆盖：生命周期管理、结构变更、修改、枚举、Bulk/Batch、低层 API、多线程（JobScheduler）、Events、CommandBuffer 等能力入口。

