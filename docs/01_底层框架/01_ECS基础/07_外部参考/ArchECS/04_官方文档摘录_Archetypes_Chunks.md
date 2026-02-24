---
文档类型: 外部参考
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - 外部参考 - ArchECS - Archetypes & Chunks
状态: 草案
---

# Archetypes 与 Chunks 摘录

- 来源: https://arch-ecs.gitbook.io/arch/documentation/archetypes-and-chunks
- 说明: 本文为官方 Archetypes & Chunks 页面节选，非完整镜像。

## 1 Archetype（摘要）

- Archetype 可以类比数据库中的表：同一组件结构的实体会被归组到同一 archetype。
- 实体的组件结构决定它属于哪个 archetype；新增/移除组件会导致实体迁移到另一个 archetype。

示例（节选）：

```csharp
var dwarf = world.Create(new Dwarf(), new Position(), new Velocity());
var elf = world.Create(new Elf(), new Position(), new Velocity());

var miningDwarf = world.Create(new Dwarf(), new Position(), new Velocity(), new Pickaxe());
```

## 2 Chunk（摘要）

- Chunk 是实体与组件的实际存储位置；一个 archetype 由多个 chunk 组成。
- 官方描述中，每个 chunk 的基础大小为 16KB（可视为与 L1 cache 友好的默认选择），用于高效加载与遍历。
- chunk 规模会根据实体的组件组成动态调整：当单个 chunk 能容纳的实体太少时，会按基础大小的倍数增大。
- chunk 基础大小与每个 chunk 最小实体数可在创建 World 时配置（见 `World.Create(chunkSizeInBytes, minimumAmountOfEntitiesPerChunk, ...)`）。

## 3 结论（用于 Ludots 吸收）

- “结构相同聚拢、结构变更迁移”的模型，要求玩法系统避免高频结构抖动，否则会造成 chunk 迁移与 archetype 碎片化。
- 组件设计需要关注体积与热度：热组件越小、越聚拢，chunk 的有效实体密度越高，遍历越快。
