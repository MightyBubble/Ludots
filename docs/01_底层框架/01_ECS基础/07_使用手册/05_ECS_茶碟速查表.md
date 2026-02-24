---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - ECS 茶碟速查表
状态: 草案
---

# ECS 茶碟速查表 使用手册

# 1 快速开始
## 1.1 适用人群与目标
面向编写 ECS 系统的日常速查：用最少时间确认“写法正确、0GC、结构变更安全、并行边界清晰”。
## 1.2 前置条件
-
  - 已阅读 `07_使用手册/01_Query与System.md` 与 `07_使用手册/02_CommandBuffer与结构变更.md`。
## 1.3 最小可用示例

最小系统模板（单线程遍历 + 结构变更集中回放）：

```csharp
private static readonly QueryDescription Q = new QueryDescription().WithAll<Position, Velocity>();
private readonly CommandBuffer _cb = new CommandBuffer(256);

public void Update(World world)
{
    world.Query(in Q, (Entity e, ref Position pos, ref Velocity vel) =>
    {
        pos.X += vel.Dx;
        pos.Y += vel.Dy;
        if (vel.Dx == 0 && vel.Dy == 0) _cb.Add<IdleTag>(e);
    });

    _cb.Playback(world);
}
```

# 2 核心概念与输入输出
## 2.1 术语与约定
-
  - 热组件：高频读写的数据列，必须紧凑、值类型、0GC。
  - 结构变更：Create/Destroy/Add/Remove 等导致 archetype 迁移的操作。
  - 安全阶段：不在 Query/Job 迭代中执行结构变更的阶段。
## 2.2 输入说明
-
  - 输入是 World + QueryDescription + 只读配置；结构变更意图写入 CommandBuffer。
## 2.3 输出产物
-
  - 输出是组件就地修改；结构变更由 CommandBuffer 在安全阶段回放。

# 3 常用场景
## 3.1 场景 A：写一个 0GC 系统
检查清单：

- QueryDescription 定义为 `static readonly` 或系统字段，不在 Update 中 new。
- Query 回调不捕获闭包、不用 LINQ、不分配 List/Dictionary/临时数组。
- 热组件禁止托管引用字段（class/string/List/Dictionary 等）。

## 3.2 场景 B：批量结构变更与回放
检查清单：

- 遍历阶段只写 CommandBuffer：`Add/Remove/Destroy/Create/Set`。
- 并行阶段（若使用）必须先 Complete，再 Playback。
- CommandBuffer 实例复用，按系统规模预估 initialCapacity，避免高峰期扩容。

## 3.3 场景 C：并行迭代与安全阶段切分
检查清单：

- 启动阶段设置 `World.SharedJobScheduler`，未设置就 fail-fast，不做 silent fallback。
- 并行阶段只读或写“本实体组件列”，禁止写共享可变状态（或必须做显式归约）。
- 阶段顺序固定：ParallelQuery/InlineParallelChunkQuery → Complete → CommandBuffer.Playback。

# 4 命令与参数参考
## 4.1 命令列表
本模块无命令行命令。
## 4.2 参数说明
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| initialCapacity | int | 否 | 128 | CommandBuffer 初始容量，按系统规模调优 |
## 4.3 返回码与失败策略
-
  - SharedJobScheduler 缺失：直接抛异常并修复启动绑定，不做“悄悄改单线程”。
  - 超出事件预算：丢弃并统计 dropped，禁止无限增长导致 GC。

# 5 产物消费与联动
## 5.1 运行时如何消费产物
-
  - 组件更新会被后续系统读取；结构变更回放会影响后续系统 Query 匹配。
## 5.2 与配置/Mod/VFS 的关系
-
  - 频率/预算/开关必须走配置管线并可被 Mod 覆盖；配置非法应 fail-fast。

# 6 诊断与常见问题
## 6.1 常见错误与定位方法
- 每帧 GC 抖动：检查闭包捕获、LINQ、List 分配、foreach+接口枚举。
- 迭代中结构变更：改为 CommandBuffer；必要时拆系统阶段。
- 并行阶段写共享状态：改为两阶段归约（并行写局部、主线程合并）。
## 6.2 性能与规模上界
-
  - 性能上界由热组件体积、archetype 碎片度、chunk 迁移频率决定；优先减少结构抖动与飞线。

# 7 安全与破坏性操作
## 7.1 默认行为
- 默认不做向后兼容与静默 fallback；错误应该直接暴露并定位。
- 默认禁止在 Query/Job 中结构变更。
## 7.2 保护开关与回滚方式
-
  - 预算熔断：事件/结构变更可丢弃并统计 dropped，用于压测与异常隔离。

# 8 关联文档与代码入口
## 8.1 工具设计/接口/配置文档链接
-
  - `docs/01_底层框架/01_ECS基础/07_使用手册/01_Query与System.md`
  - `docs/01_底层框架/01_ECS基础/07_使用手册/02_CommandBuffer与结构变更.md`
  - `docs/01_底层框架/01_ECS基础/07_使用手册/03_JobScheduler与并行迭代.md`
  - `docs/01_底层框架/01_ECS基础/07_使用手册/04_事件与EventBus.md`
## 8.2 代码入口
-
  - World QueryCache：`src/Libraries/Arch/src/Arch/Core/World.cs`
  - CommandBuffer：`src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs`
  - Jobs：`src/Libraries/Arch/src/Arch/Core/Jobs/*`
  - GameplayEventBus：`src/Core/Gameplay/GAS/GameplayEventBus.cs`
