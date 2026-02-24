---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - Why Arch
状态: 草案
---

# Why Arch 摘录

- 来源: https://arch-ecs.gitbook.io/arch
- 说明: 本文为官方 Why Arch 页面节选，保留与 Ludots 相关的核心描述与示例，非完整镜像。

## 1 项目定位与要解决的问题

- Arch 是一个高性能、极简的 C# ECS，实现基于 Archetype 与 Chunk 的数据布局，用于游戏开发与数据导向编程。
- 目标是在 .NET 生态下提供接近 C++/Rust ECS 库的缓存效率与迭代速度。
- 支持 .NETStandard 2.1 与 .NET 8，可用于 Unity、Godot 或任意 C# 项目。

## 2 官方示例（节选）

```csharp
using Arch;

// Components
public record struct Position(float X, float Y);
public record struct Velocity(float Dx, float Dy);

// Create a world and an entity with position and velocity.
using var world = World.Create();
var player = world.Create(new Position(0,0), new Velocity(1,1));

// Enumerate all entities with Position & Velocity to move them
var query = new QueryDescription().WithAll<Position,Velocity>();
world.Query(in query, (Entity entity, ref Position pos, ref Velocity vel) => {
    pos.X += vel.Dx;
    pos.Y += vel.Dy;
    Console.WriteLine($"Moved player: {entity.Id}");
});
```

## 3 关键卖点（摘要）

- 高速: 最佳缓存效率、迭代与分配速度，同级于 C++/Rust ECS。
- 极简: 只提供必要抽象（World/Entity/Component/Query 等），便于项目方自行封装更高层框架。
- 易用: API 设计自解释、非侵入，支持泛型与非泛型调用形式。

