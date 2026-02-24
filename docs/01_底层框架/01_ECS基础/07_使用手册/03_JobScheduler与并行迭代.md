---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - JobScheduler与并行迭代
状态: 草案
---

# JobScheduler与并行迭代 使用手册

# 1 快速开始
## 1.1 适用人群与目标
面向需要在 Arch 中使用并行 Query/Chunk 迭代的开发者。目标是在保证正确性的前提下，把可并行的热路径计算并行化，同时保持 0GC 与结构变更安全边界清晰。
## 1.2 前置条件
-
  - 已理解 Query 与结构变更边界：`07_使用手册/01_Query与System.md`、`07_使用手册/02_CommandBuffer与结构变更.md`。
  - 已在启动阶段为 `World.SharedJobScheduler` 赋值（否则并行 API 会 fail-fast 抛异常）。
## 1.3 最小可用示例

以下示例展示“绑定 SharedJobScheduler + 并行按 chunk 执行 + 完成后再结构变更”的最小模式：

```csharp
World.SharedJobScheduler = new JobScheduler(Environment.ProcessorCount);

private static readonly QueryDescription Q = new QueryDescription().WithAll<Position, Velocity>();

public void Update(World world)
{
    world.InlineParallelQuery<MoveForEach>(in Q);
    _cb.Playback(world);
}

public struct MoveForEach : IForEach
{
    public void Update(Entity e)
    {
        ref var pos = ref e.Get<Position>();
        ref var vel = ref e.Get<Velocity>();
        pos.X += vel.Dx;
        pos.Y += vel.Dy;
    }
}
```
# 2 核心概念与输入输出
## 2.1 术语与约定
## 2.2 输入说明
-
  - SharedJobScheduler：Arch 的并行执行单例（用于并行迭代），未初始化则 fail-fast。
  - 并行阶段：只读或写“本实体组件列”的阶段。
  - 安全阶段：完成并行后，集中执行结构变更（CommandBuffer.Playback）。
## 2.3 输出产物
-
  - 输入是 QueryDescription + 需要执行的 per-entity 或 per-chunk 逻辑（IForEach/IChunkJob）。
  - 并行逻辑必须是可分区的：不能写共享可变状态（或必须做显式归约）。

-
  - 输出是组件数据被并行更新；结构变更意图应写入 CommandBuffer，等待安全阶段回放。
# 3 常用场景
## 3.1 场景 A：并行只读扫描
## 3.2 场景 B：并行写本实体热组件
-
  - 用并行 chunk 遍历生成统计（计数、最大值等），最终把结果写回到单线程的聚合点。
## 3.3 场景 C：并行阶段与结构变更阶段切分
-
  - 典型例子：Movement、属性聚合、AOI 更新等，只写 Position/Velocity/Flags 等值类型热组件。

-
  - 禁止在并行 Job 内调用 World 的结构变更 API。
  - 推荐固定阶段顺序：并行迭代 → 完成 Job → CommandBuffer.Playback。
# 4 命令与参数参考
## 4.1 命令列表
## 4.2 参数说明
本模块无命令行命令。
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
## 4.3 返回码与失败策略
| World.SharedJobScheduler | JobScheduler | 是 | 无 | 并行迭代单例，未赋值则并行 API 会抛异常 |

-
  - 若 SharedJobScheduler 未初始化：并行 API 会 fail-fast 抛异常，必须在启动阶段修复。
  - 并行执行中若写入共享状态造成竞争：按 bug 定位与修复，不通过锁/同步器在热路径“硬补丁”。
# 5 产物消费与联动
## 5.1 运行时如何消费产物
## 5.2 与配置/Mod/VFS 的关系
-
  - 并行更新后的组件数据在同一 Tick 后续系统可见；需要确定性阶段边界时，由 Engine 的系统调度保证顺序。

-
  - 并行开关与线程数可配置化，但默认不做 silent fallback（配置非法应 fail-fast）。
# 6 诊断与常见问题
## 6.1 常见错误与定位方法
## 6.2 性能与规模上界
-
  - 异常 “SharedJobScheduler is missing”：检查启动阶段是否赋值 `World.SharedJobScheduler`。
  - 并行 Query 从非主线程调用：`World.Jobs` 明确标注 “NOT thread-safe”，只能从主线程发起调度。
  - 并行阶段出现结构变更：改为写入 CommandBuffer，在 Complete 后回放。

-
  - 并行收益上界由“可并行比例 + chunk 数量 + CPU 核心数”决定；若 archetype 碎片导致 chunk 很少，收益会显著下降。
# 7 安全与破坏性操作
## 7.1 默认行为
## 7.2 保护开关与回滚方式
-
  - 默认不允许并行阶段结构变更；默认不允许从非主线程发起并行 Query 调度。

-
  - 最小回滚：关闭并行路径（回退到单线程 Query），但必须以显式配置开关切换，禁止静默 fallback。
# 8 关联文档与代码入口
## 8.1 工具设计/接口/配置文档链接
## 8.2 代码入口
-
  - `docs/01_底层框架/01_ECS基础/07_使用手册/02_CommandBuffer与结构变更.md`

-
  - 并行 Query 与 fail-fast 约束：`src/Libraries/Arch/src/Arch/Core/Jobs/World.Jobs.cs`
  - Job 基础接口与实现：`src/Libraries/Arch/src/Arch/Core/Jobs/Jobs.cs`
