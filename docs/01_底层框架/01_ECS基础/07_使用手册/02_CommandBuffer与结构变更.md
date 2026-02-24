---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - CommandBuffer与结构变更
状态: 草案
---

# CommandBuffer与结构变更 使用手册

# 1 快速开始
## 1.1 适用人群与目标
面向需要在 ECS 系统中进行“创建/销毁实体、增删组件”等结构变更的开发者。目标是把结构变更从遍历阶段剥离出来，在安全阶段集中回放，避免遍历失效、并行数据竞争与隐藏 GC。
## 1.2 前置条件
-
  - 熟悉 Query 遍历写法：`07_使用手册/01_Query与System.md`。
  - 了解哪些操作属于结构变更：创建/销毁实体、Add/Remove 组件、导致 archetype 迁移的操作。
## 1.3 最小可用示例

推荐模式：系统遍历阶段只记录意图到 CommandBuffer，Tick 尾部统一 `Playback`。

```csharp
private readonly CommandBuffer _cb = new CommandBuffer(initialCapacity: 1024);
private static readonly QueryDescription KillQuery = new QueryDescription().WithAll<Health>().None<DeadTag>();

public void Update(World world)
{
    world.Query(in KillQuery, (Entity e, ref Health hp) =>
    {
        if (hp.Value <= 0) _cb.Add<DeadTag>(e);
    });

    _cb.Playback(world);
}
```

# 2 核心概念与输入输出
## 2.1 术语与约定
-
  - 结构变更：会改变实体组件结构并触发 archetype/chunk 迁移的操作。
  - 安全阶段：不存在 Query 迭代与并行 Job 运行的阶段，可以执行结构变更。
  - CommandBuffer：记录对实体的 Create/Destroy/Add/Remove/Set 意图，并在 `Playback(World)` 时一次性应用。
## 2.2 输入说明
-
  - 输入是结构变更意图（实体 + 操作 + 可选组件值）。
  - 结构变更意图建议按系统维度维护 buffer，并复用同一个实例，避免频繁分配。
## 2.3 输出产物
-
  - 输出是 World 的结构发生变化（新实体/被销毁实体/组件结构迁移）。
  - 如果启用了 Arch 的 EVENTS 标志，Playback 过程会触发组件新增/设置等事件回调。

# 3 常用场景
## 3.1 场景 A：批量添加/移除组件
-
  - 在 Query 遍历中只写 `_cb.Add<T>(entity)` / `_cb.Remove<T>(entity)`，不要直接 `world.Add/Remove`。
  - 同一实体同一组件的重复 Add/Set 以最后一次为准（buffer 覆写旧值）。
## 3.2 场景 B：批量创建/销毁实体
-
  - 创建：使用 `_cb.Create(ComponentType[])` 记录创建意图，Playback 才真正生成实体。
  - 销毁：使用 `_cb.Destroy(entity)` 记录销毁意图，Playback 统一执行 `world.Destroy`。
## 3.3 场景 C：与并行 Job 协作
-
  - 并行阶段（ParallelQuery/InlineParallelChunkQuery）只读或写本实体组件列。
  - 结构变更阶段必须在 Job 完成之后：JobHandle.Complete → CommandBuffer.Playback。

# 4 命令与参数参考
## 4.1 命令列表
本模块无命令行命令。
## 4.2 参数说明
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| initialCapacity | int | 否 | 128 | CommandBuffer 内部容器初始容量；按系统规模预估 |
| dispose | bool | 否 | true | `Playback(world, dispose)` 是否在回放后清空记录 |
## 4.3 返回码与失败策略
-
  - 若 `Playback` 中发现目标实体已死亡，Arch 内部会断言；Ludots 应把这类问题当作逻辑 bug 直接修复，不做静默忽略。
  - 若需要“可恢复”策略，应在写入 buffer 前做校验与统计，并输出可定位的证据入口。

# 5 产物消费与联动
## 5.1 运行时如何消费产物
-
  - Playback 发生的结构变化会影响同一 Tick 后续系统的 Query 匹配结果。
  - 需要稳定阶段边界时，用系统分组或 Engine 的阶段调度保证“先遍历、后回放”的顺序。
## 5.2 与配置/Mod/VFS 的关系
-
  - buffer 容量、回放预算等可配置化（走 ConfigPipeline），但默认不做 silent fallback。

# 6 诊断与常见问题
## 6.1 常见错误与定位方法
-
  - 在 Query 回调中直接结构变更：改为 CommandBuffer；若必须立即生效，拆阶段或拆系统。
  - CommandBuffer 每帧 new：改为系统字段复用；必要时在高峰前预热容量。
  - 回放顺序错误：确认 Job 已 Complete，再回放结构变更。
## 6.2 性能与规模上界
-
  - 回放成本与记录条目数近似线性；应避免把“高频临时状态”用 Add/Remove 表达。
  - 高频临时效果优先写在宿主组件数据内（值类型字段/位标记），避免 archetype 频繁迁移。

# 7 安全与破坏性操作
## 7.1 默认行为
-
  - 默认只允许在安全阶段回放结构变更；并行阶段禁止结构变更。
## 7.2 保护开关与回滚方式
-
  - 最小保护开关：允许丢弃 buffer（不回放）并输出 dropped 统计，用于压测与故障隔离。
  - 回滚方式：清空 buffer 并在下一帧重新计算意图，禁止通过“静默忽略错误实体”继续运行。

# 8 关联文档与代码入口
## 8.1 工具设计/接口/配置文档链接
-
  - `docs/01_底层框架/01_ECS基础/07_使用手册/03_JobScheduler与并行迭代.md`
## 8.2 代码入口
-
  - CommandBuffer 实现：`src/Libraries/Arch/src/Arch/Buffer/CommandBuffer.cs`
  - 结构变更标注：`src/Libraries/Arch/src/Arch/Core/Utils/StructuralChangeAttribute.cs`
