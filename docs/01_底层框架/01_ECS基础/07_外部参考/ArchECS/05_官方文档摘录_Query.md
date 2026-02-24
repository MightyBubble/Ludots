---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - Query
状态: 草案
---

# Query 摘录

- 来源: https://arch-ecs.gitbook.io/arch/documentation/query
- 说明: 本文为官方 Query 页面节选，非完整镜像。

## 1 QueryDescription（摘要）

- Query 是 World 的一个视图，通过 QueryDescription 描述“要匹配的实体结构”。
- 可表达三类过滤：必须拥有（WithAll）、可拥有任意一个（WithAny）、必须不拥有（None）。
- 还支持 WithExclusive：只匹配“恰好拥有这些组件、没有多余组件”的实体。
- QueryDescription 的泛型方法最多支持 25 个类型参数，并支持链式组合。

## 2 官方示例（节选）

遍历拥有 Position 与 Velocity 的实体：

```csharp
var movementQuery = new QueryDescription().WithAll<Position, Velocity>();
world.Query(in movementQuery, (Entity entity, ref Position pos, ref Velocity vel) => {
    pos += vel;
});
```

排除某类组件：

```csharp
var letDwarfsAndHumansMine = new QueryDescription().WithAll<Pickaxe>().None<Elf>();
world.Query(in letDwarfsAndHumansMine, (ref Pickaxe pickaxe) => {
    MineSomeOres(pickaxe, FindNextRock());
});
```

匹配任意武器（WithAny）：

```csharp
var patrol = new QueryDescription().WithAny<Bow, Sword>();
world.Query(in patrol, (Entity entity) => {
    ref var bow = entity.TryGet<Bow>(out var hasBow);
    ref var sword = entity.TryGet<Sword>(out var hasSword);
    if (hasBow) bow.Attack();
    if (hasSword) sword.Attack();
});
```

精确匹配结构（WithExclusive）：

```csharp
var makeWeakDwarfsExile = new QueryDescription().WithExclusive<Dwarf, Position, Velocity>();
world.Query(in makeWeakDwarfsExile, (Entity entity) => { Exile(entity); });
```
