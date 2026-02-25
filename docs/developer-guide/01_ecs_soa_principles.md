# ECS 开发实践与 SOA 原则

Ludots 的核心游戏循环基于 [Arch](https://github.com/genaray/Arch) ECS 库构建。本指南阐述了在 Ludots 中进行 ECS 开发的最佳实践和设计原则。

## 1 核心原则：数据导向与 SoA

为了最大化性能（特别是缓存命中率），我们严格遵循数据导向设计与结构数组原则。下文简称 DOD（Data-Oriented Design）与 SoA（Structure of Arrays）。

*   **SoA**: 将组件数据按类型分别存储在连续的数组中，而不是将每个实体的所有数据存储在一个对象中。Arch ECS 内部自动管理这种布局。
*   **Zero-GC**: 在游戏循环的核心路径（System Update）中，禁止分配任何托管堆内存（`new class`）。所有运行时数据必须是 `struct` 或非托管内存。
*   **Cache Friendly**: 系统应当线性遍历组件数组，避免随机内存访问（禁止“内存飞线”式访问）。

## 2 组件设计规范

组件必须是 **Blittable Struct**（纯值类型结构体），且不包含引用类型字段（如 `string`, `class`, `List<T>`）。

### 2.1 基础组件示例

```csharp
using Arch.Core;
using FixPointCS;

// 位置组件：仅包含数据，使用定点数
public struct WorldPositionCm
{
    public Fix64Vec2 Value;
}

// 速度组件
public struct Velocity
{
    public IntVector2 Value; // 使用整数向量避免浮点误差
}

// 标签组件：空结构体，用于标记实体状态
public struct IsPlayerTag { }
```

### 2.2 命名规范
*   **数据组件**: 以后缀 `Cm` 结尾（推荐）或直接使用名词（如 `Velocity`）。
*   **标签组件**: 必须以 `Tag` 结尾（如 `IsDeadTag`），用于布尔状态标记。
*   **事件组件**: 必须以 `Event` 结尾（如 `CollisionEvent`），通常在帧末清理。

## 3 系统开发规范

系统负责逻辑处理。在 Ludots 中，系统通过 `SystemGroup` 进行严格分层，确保执行顺序的确定性。

### 3.1 系统分组

所有系统必须归属于以下分组之一（定义于 `GameEngine.SystemGroup`）：

1.  **SchemaUpdate**：运行时注册与 schema 变更（属性、Graph 等）。
2.  **InputCollection**：输入与状态收集（时钟、输入缓冲等）。
3.  **PostMovement**：移动后同步与空间更新（SSOT 更新与空间索引刷新）。
4.  **AbilityActivation**：能力激活与指令管线入口。
5.  **EffectProcessing**：效果处理与响应链主循环。
6.  **AttributeCalculation**：属性聚合与绑定。
7.  **DeferredTriggerCollection**：延迟触发器收集与处理。
8.  **Cleanup**：清理与帧末收束。
9.  **EventDispatch**：事件分发。
10. **ClearPresentationFlags**：仅服务于表现层的脏标记位清理。

### 3.2 编写一个系统

推荐继承自 `BaseSystem` 或直接实现 `ISystem`。

```csharp
using Arch.Core;

public class MovementSystem : BaseSystem<World, float>
{
    // 定义查询描述：获取所有具有位置和速度的实体
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<WorldPositionCm, Velocity>();

    public MovementSystem(World world) : base(world) { }

    public override void Update(in float deltaTime)
    {
        // 使用 Query 遍历实体
        World.Query(in _query, (ref WorldPositionCm pos, ref Velocity vel) => 
        {
            // 逻辑更新：位置 += 速度 * 时间
            pos.Value += vel.Value * (Fix64)deltaTime;
        });
    }
}
```

## 4 查询与遍历最佳实践

### 4.1 QueryDescription 缓存

*   不要每次 `Update` 都创建新的 `QueryDescription`。
*   在构造函数或字段初始化阶段缓存为 `static readonly` / `private readonly`。
*   查询签名稳定时，优先复用同一个 Query 描述，避免热路径重复构建。

### 4.2 Inline Query（热路径优先）

`InlineEntityQuery` / `InlineQuery` 更适合高频、稳定组件签名、需要最小化委托开销的路径。

```csharp
private readonly QueryDescription _trackedQuery = new QueryDescription()
    .WithAll<WorldPositionCm, SpatialCellRef>();

public override void Update(in float dt)
{
    var moveJob = new MoveJob { Partition = _partition, Spec = _spec };
    World.InlineEntityQuery<MoveJob, WorldPositionCm, SpatialCellRef>(in _trackedQuery, ref moveJob);
}

private struct MoveJob : IForEachWithEntity<WorldPositionCm, SpatialCellRef>
{
    public ISpatialPartitionWorld Partition;
    public WorldSizeSpec Spec;

    public void Update(Entity entity, ref WorldPositionCm pos, ref SpatialCellRef cellRef)
    {
        // 省略业务逻辑
    }
}
```

### 4.3 Chunk 迭代（批处理优先）

当你需要批量处理（统计、分块写入、Span 访问）时，优先使用 chunk 迭代。

```csharp
foreach (ref var chunk in World.Query(in _untrackedQuery))
{
    ref var entityFirst = ref chunk.Entity(0);
    var positions = chunk.GetSpan<WorldPositionCm>();

    foreach (var index in chunk)
    {
        var entity = Unsafe.Add(ref entityFirst, index);
        var worldCm = positions[index].Value.ToWorldCmInt2();
        _commandBuffer.Add(entity, new SpatialCellRef());
    }
}
```

### 4.4 禁止内存飞线

“内存飞线”指热路径里跨容器、跨实体、随机跳转式访问，导致缓存命中率下降。ECS 代码必须避免以下模式：

*   在 `World.Query` 循环体中反复 `World.Get/Has/TryGet` 非必要组件，打断线性访问。
*   先收集 `Entity[]` 再二次随机回查组件。
*   在同一帧中混用大量 Dictionary/HashSet 随机访问作为主更新路径。

推荐模式：

*   通过查询签名一次性声明所需组件，主循环里只做 `ref` 访问。
*   需要额外随机访问时，先判断是否能拆到非热路径或独立阶段。
*   必须回查时，限制在低频分支，并保证主更新仍以 chunk 线性遍历为主。

## 5 结构变更纪律与 CommandBuffer

### 5.1 杜绝热路径结构变更

结构变更（structural changes）包括但不限于：

*   `World.Create`
*   `World.Destroy`
*   `World.Add`
*   `World.Remove`
*   任何导致实体 Archetype 迁移的操作

规则：

*   禁止在查询热循环中直接做结构变更。
*   结构变更必须集中在明确阶段（例如系统尾部、帧阶段边界）统一回放。
*   如果逻辑复杂（多阶段 Effect/GAS），用阶段化回放而非“边查边改”。

### 5.2 活用 CommandBuffer

统一采用“记录 -> 回放”模式：

1.  查询阶段只记录命令，不动 World 结构。
2.  阶段末检查 `_commandBuffer.Size > 0`。
3.  一次 `Playback(World)` 提交结构变更。

```csharp
private readonly CommandBuffer _commandBuffer = new();

private void InitializeMissingPrevPos()
{
    World.Query(in _needsPrevPosQuery, (Entity entity, ref Position2D position) =>
    {
        _commandBuffer.Add(entity, new PreviousPosition2D { Value = position.Value });
    });

    if (_commandBuffer.Size > 0)
    {
        _commandBuffer.Playback(World);
    }
}
```

### 5.3 回放时机建议

*   单系统内：一次 `Update` 最多 1~2 次回放，避免碎片化频繁 Playback。
*   多阶段系统：按照阶段推进集中回放（例如先收集，再统一注册/销毁/挂载）。
*   不要把 `Playback` 放进实体循环内部。

## 6 确定性

Ludots 致力于提供确定性的模拟结果（用于回放和网络同步）。

*   **不要把 `float` / `double` 写入决定性状态**：决定性状态与跨帧累积值使用 `Fix64` 或 `int`。`dt` 在当前调度中为 `float`，仅用于本帧计算，不应被存储为长期状态。
*   **禁止使用 `System.Random`**: 必须使用核心提供的确定性随机数生成器。
*   **禁止依赖字典遍历顺序**: `Dictionary` 的遍历顺序是不确定的，如需遍历请先排序或使用 `SortedDictionary` / 列表。

## 7 参考实现（仓库内）

*   Inline Query 示例：`src/Core/Systems/SpatialPartitionUpdateSystem.cs`
*   Chunk 迭代 + Span 示例：`src/Core/Systems/SpatialPartitionUpdateSystem.cs`
*   CommandBuffer 记录/回放示例：`src/Core/Ludots.Physics2D/Systems/IntegrationSystem2D.cs`
*   多阶段延迟结构变更示例：`src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs`
*   Arch 结构变更约束说明：`src/Libraries/Arch/src/Arch/Core/World.cs`
