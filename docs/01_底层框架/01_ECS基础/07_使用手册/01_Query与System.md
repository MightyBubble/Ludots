---
文档类型: 使用手册
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 底层框架 - ECS基础 - Query与System
状态: 草案
---

# Query与System 使用手册

# 1 快速开始
## 1.1 适用人群与目标
面向需要编写 Ludots ECS 系统的开发者，目标是用 Arch 的 Query 高效遍历组件数据，并在不引入 GC 的前提下组织系统逻辑边界。
## 1.2 前置条件
-
  - 运行时已有 `World` 实例（由 Engine 创建并驱动 Tick）。
  - 已定义组件为值类型（避免托管引用飞线）。
## 1.3 最小可用示例

以下示例展示推荐的“静态 QueryDescription + Update 内复用”的最小形态：

```csharp
private static readonly QueryDescription MoveQuery =
    new QueryDescription().WithAll<Position, Velocity>().None<DeadTag>();

public void Update(World world)
{
    world.Query(in MoveQuery, (ref Position pos, ref Velocity vel) =>
    {
        pos.X += vel.Dx;
        pos.Y += vel.Dy;
    });
}
```

# 2 核心概念与输入输出
## 2.1 术语与约定
-
  - World：实体容器与调度入口。
  - Component：以数据为中心的值类型结构体；热路径禁止托管引用字段。
  - QueryDescription：结构过滤器（WithAll/WithAny/None/WithExclusive）的组合。
  - System：围绕 Query 扫描并读写组件数据的逻辑单元；结构变更必须集中在安全阶段执行。
## 2.2 输入说明
-
  - 输入是 World + QueryDescription（以及可选的外部只读参数，例如配置、只读表）。
  - Query 回调内只读/只写本实体组件列；禁止写共享全局状态（除非做明确的分阶段归约）。
## 2.3 输出产物
-
  - 直接输出：组件数据被就地更新（例如 Position、Velocity）。
  - 间接输出：若需要结构变更或事件，输出“意图”到集中通道（CommandBuffer 或事件组件流）。

# 3 常用场景
## 3.1 场景 A：遍历与更新热组件
-
  - Query 回调只传 `ref` 组件参数（不需要 `Entity` 就别传），减少解包与分支。
  - 把业务分支拆成多个 Query，避免在一个 Query 回调中写大段 if-else。
## 3.2 场景 B：过滤与分支拆分
-
  - WithAll 表达必须拥有的热组件结构，None 表达排除条件（例如 DeadTag）。
  - WithAny 适合“任意武器/任意能力”这种松散结构，但回调内需要用 `TryGet<T>(out has)` 做分支。
  - WithExclusive 只匹配“恰好这些组件”的实体，容易误用，除非你确实在建模一种“专用结构实体”。
## 3.3 场景 C：统计与只读扫描
-
  - 只需要数量时优先用 `World.CountEntities(in queryDescription)`，避免拉实体集合。
  - 统计场景尽量走“按 chunk 线性遍历 + 累加值类型计数”，避免临时 List/Dictionary。

# 4 命令与参数参考
## 4.1 命令列表
本模块无命令行命令，主要是 API 约束与写法。
## 4.2 参数说明
| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---:|---|---|
| QueryDescription | 结构过滤 | 是 | 无 | 描述要匹配的实体结构，建议 `static readonly` 复用 |
## 4.3 返回码与失败策略
-
  - 过滤表达错误：让 Query 匹配不到实体是正常情况，不做静默 fallback 到其他口径。
  - 结构变更时机错误：不在 Query 回调中直接 Add/Remove/Create/Destroy，必须 fail-fast 或转移到集中通道。

# 5 产物消费与联动
## 5.1 运行时如何消费产物
-
  - 组件更新会在同一 Tick 内被后续系统读取（按系统排序/阶段决定可见性）。
  - 结构变更与事件流由专门系统在安全阶段回放或清理。
## 5.2 与配置/Mod/VFS 的关系
-
  - Query/System 本身不直接读取 VFS；系统配置应通过 ConfigPipeline 注入为只读数据，再被系统消费。

# 6 诊断与常见问题
## 6.1 常见错误与定位方法
-
  - 每帧 new QueryDescription：检查是否把 QueryDescription 写在 Update 内；应提升为 `static readonly` 或系统字段。
  - Query 回调产生 GC：检查是否捕获闭包、使用 LINQ、分配临时集合或装箱；用性能采样验证 0GC。
  - 结构变更导致崩溃/不一致：检查是否在遍历中直接调用 World 的结构变更 API；改为 CommandBuffer。
## 6.2 性能与规模上界
-
  - 性能上界由“热组件体积 + archetype 数量 + chunk 迁移频率”决定。
  - 目标是让热路径系统稳定场景 0GC，且查询以线性 chunk 遍历为主。

# 7 安全与破坏性操作
## 7.1 默认行为
-
  - 默认不做向后兼容与静默 fallback：过滤写错就该查清楚原因，而不是退化到其他查询口径。
  - 默认禁止在 Query 回调中做结构变更（创建/销毁/增删组件）。
## 7.2 保护开关与回滚方式
-
  - 结构变更通过集中通道（CommandBuffer）回放，回放前可做预算与统计；回滚以“撤销回放/清空 buffer”为最小手段。

# 8 关联文档与代码入口
## 8.1 工具设计/接口/配置文档链接
-
  - `docs/01_底层框架/01_ECS基础/07_使用手册/02_CommandBuffer与结构变更.md`
  - `docs/01_底层框架/01_ECS基础/07_使用手册/03_JobScheduler与并行迭代.md`
## 8.2 代码入口
-
  - Query 缓存入口：`src/Libraries/Arch/src/Arch/Core/World.cs`（`World.Query(in QueryDescription)`）
  - Query 并行入口：`src/Libraries/Arch/src/Arch/Core/Jobs/World.Jobs.cs`
