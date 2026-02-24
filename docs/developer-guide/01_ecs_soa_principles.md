# ECS 开发实践与 SOA 原则

Ludots 的核心游戏循环基于 [Arch](https://github.com/genaray/Arch) ECS 库构建。本指南阐述了在 Ludots 中进行 ECS 开发的最佳实践和设计原则。

## 1 核心原则：数据导向与 SoA

为了最大化性能（特别是缓存命中率），我们严格遵循数据导向设计与结构数组原则。下文简称 DOD（Data-Oriented Design）与 SoA（Structure of Arrays）。

*   **SoA**: 将组件数据按类型分别存储在连续的数组中，而不是将每个实体的所有数据存储在一个对象中。Arch ECS 内部自动管理这种布局。
*   **Zero-GC**: 在游戏循环的核心路径（System Update）中，禁止分配任何托管堆内存（`new class`）。所有运行时数据必须是 `struct` 或非托管内存。
*   **Cache Friendly**: 系统应当线性遍历组件数组，避免随机内存访问。

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

## 4 查询优化

*   **缓存 QueryDescription**: 不要每次 `Update` 都创建新的 `QueryDescription`，应在构造函数中创建并缓存为 `static readonly` 或 `private readonly` 字段。
*   **使用 `Entity` 引用**: 如果需要实体 ID，使用 `.WithEntity()` 查询方法。
*   **避免结构体复制**: 在 Lambda 或循环中，始终使用 `ref` 关键字访问组件（如 `ref WorldPositionCm pos`），避免值拷贝开销。

## 5 确定性

Ludots 致力于提供确定性的模拟结果（用于回放和网络同步）。

*   **不要把 `float` / `double` 写入决定性状态**：决定性状态与跨帧累积值使用 `Fix64` 或 `int`。`dt` 在当前调度中为 `float`，仅用于本帧计算，不应被存储为长期状态。
*   **禁止使用 `System.Random`**: 必须使用核心提供的确定性随机数生成器。
*   **禁止依赖字典遍历顺序**: `Dictionary` 的遍历顺序是不确定的，如需遍历请先排序或使用 `SortedDictionary` / 列表。
