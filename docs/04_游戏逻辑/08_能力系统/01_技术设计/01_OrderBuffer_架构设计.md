---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Order缓冲与队列
状态: 已实现
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/00_总览.md
  - docs/06_交互层/13_输入系统/02_InputOrderMapping_架构设计.md
---

# OrderBuffer 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 Order 系统的完整架构设计，包括：
- per-Entity 的 Order 队列管理（OrderBuffer）
- Order 提交模式（Immediate / Queued）
- Order 激活后的状态表示（Tag + Blackboard）
- OrderState Tag 与 TagRuleSet 的冲突处理
- AI 与玩家 Order 的统一入口

核心理念：**Order 是意图声明，激活后转换为 Tag（状态）+ Blackboard（参数）**

边界：
- 本文档涉及 Order 的缓冲、排队、提升、激活逻辑
- 不涉及 Ability 的具体执行（由 AbilityTimeline 负责，见 `10_AbilityTimeline_架构设计.md`）
- 不涉及 Input 到 Order 的映射（由 InputOrderMapping 负责）

非目标：
- 网络同步（后续文档定义）
- 客户端预测与回滚（后续文档定义）

## 1.2 设计目标

1. **统一性**：AI 和玩家发 Order 使用相同入口，最终效果等价
2. **可配置**：每种 Order 类型可独立配置队列策略、优先级、缓冲窗口
3. **可查询**：通过 Tag 查询 Entity 的 Order 状态（执行中、排队中）
4. **冲突处理**：通过 TagRuleSet 配置 Order 间的互斥/打断/替换规则
5. **预输入支持**：支持连按同一 Order 形成排队，支持 Shift+ 强制排队
6. **松耦合**：Order 参数存储在 Blackboard，执行系统通过 Tag 和 Blackboard 获取数据

## 1.3 核心架构

```
┌──────────────────────────────────────────────────────────────────┐
│                      Order = 意图声明                             │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ OrderSubmitter.Submit(order, mode)                         │ │
│  │   └─ mode: Immediate (普通) / Queued (Shift+)              │ │
│  └────────────────────────────────────────────────────────────┘ │
└────────────────────────────┬─────────────────────────────────────┘
                             │
           ┌─────────────────┼─────────────────┐
           │                 │                 │
           ▼                 ▼                 ▼
    ┌────────────┐    ┌────────────┐    ┌────────────┐
    │ Activated  │    │  Queued    │    │  Rejected  │
    │            │    │            │    │            │
    │ Tag +      │    │ OrderBuffer│    │ Blocked/   │
    │ Blackboard │    │ 等待执行   │    │ QueueFull  │
    └─────┬──────┘    └─────┬──────┘    └────────────┘
          │                 │
          │                 │ PromoteNext
          │                 │
          ▼                 ▼
    ┌─────────────────────────────────────────────────────────────┐
    │                    执行系统查询                              │
    │  ┌────────────────┐  ┌────────────────┐                    │
    │  │ Query: Tag     │  │ Query:         │                    │
    │  │ HasActive?     │──│ Blackboard     │                    │
    │  │ Active.MoveTo? │  │ Move_Waypoints │                    │
    │  └────────────────┘  └────────────────┘                    │
    └─────────────────────────────────────────────────────────────┘
```

# 2 功能总览

## 2.1 术语表

| 术语 | 定义 |
|------|------|
| Order | 发给 Entity 的意图声明（如施法、移动、攻击） |
| OrderSubmitMode | 提交模式：Immediate（普通）或 Queued（Shift+） |
| OrderBuffer | per-Entity 组件，存储排队中的 Order |
| OrderTypeConfig | Order 类型配置（队列策略、优先级等） |
| OrderState Tag | 表示 Order 执行状态的 GameplayTag |
| Active Order | 当前正在执行的 Order（表现为 Tag + Blackboard） |
| Queued Order | 排队等待执行的 Order（存储在 OrderBuffer） |
| sameTypePolicy | Immediate 模式下，连按同类型 Order 的处理策略 |
| Blackboard | 存储 Order 参数的组件（位置、目标等） |

## 2.2 功能导图

```
OrderBuffer 功能
├── 提交模式
│   ├── Immediate（普通）
│   │   ├── 冲突检查 → 打断或按策略排队
│   │   └── sameTypePolicy（queue/replace/ignore）
│   └── Queued（Shift+）
│       └── 跳过冲突检查 → 直接追加队列末尾
├── 队列管理
│   ├── 入队（Enqueue）
│   ├── 提升（PromoteNext）
│   └── 过期清理（Expire）
├── 状态表示（激活后）
│   ├── GameplayTag（Order.Active.*）
│   └── Blackboard（参数数据）
└── 冲突处理
    ├── TagRuleSet.BlockedTags（阻止）
    └── TagRuleSet.RemovedTags（打断）
```

## 2.3 架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                         Order 入口                               │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │ 玩家输入      │    │ AI 决策       │    │ 脚本触发      │       │
│  │ (Shift+)     │    │              │    │              │       │
│  └──────┬───────┘    └──────┬───────┘    └──────┬───────┘       │
│         │                   │                   │               │
│         └───────────────────┼───────────────────┘               │
│                             ▼                                   │
│                   ┌──────────────────┐                          │
│                   │ OrderSubmitter   │ 统一入口                  │
│                   │ Submit(mode)     │                          │
│                   └────────┬─────────┘                          │
└────────────────────────────┼────────────────────────────────────┘
                             │
      ┌──────────────────────┼──────────────────────┐
      │ Immediate            │ Queued (Shift+)      │
      ▼                      ▼                      ▼
┌────────────┐        ┌────────────┐        ┌────────────┐
│ 冲突检查   │        │ 跳过冲突   │        │ AI Bypass  │
│ TagRuleSet │        │ 直接入队   │        │ 直接激活   │
└─────┬──────┘        └─────┬──────┘        └─────┬──────┘
      │                     │                     │
      ▼                     │                     │
┌───────────────┐           │                     │
│ 打断 / 按策略 │           │                     │
│ 排队 / 拒绝   │           │                     │
└───────┬───────┘           │                     │
        │                   │                     │
        └───────────────────┼─────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    激活 = 转换为 Tag + Blackboard                │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │ GameplayTag  │    │ Blackboard   │    │ Blackboard   │       │
│  │ Container    │    │ SpatialBuffer│    │ EntityBuffer │       │
│  │              │    │              │    │              │       │
│  │ +Active.Move │    │ Waypoints[]  │    │ TargetEntity │       │
│  │ +HasActive   │    │              │    │              │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    执行系统                                      │
│  Query<GameplayTagContainer, BlackboardSpatialBuffer>           │
│  if (tags.HasActive.MoveTo) → waypoints = spatial.Move_Waypoints│
└─────────────────────────────────────────────────────────────────┘
```

## 2.4 关联依赖

| 依赖模块 | 依赖方式 | 说明 |
|----------|----------|------|
| GameplayTagContainer | 组件 | 存储 OrderState Tag |
| BlackboardSpatialBuffer | 组件 | 存储位置/路径参数 |
| BlackboardEntityBuffer | 组件 | 存储目标 Entity |
| BlackboardIntBuffer | 组件 | 存储整数参数（如技能槽位） |
| TagRuleRegistry | 查询 | 获取 Order Tag 的冲突规则 |
| OrderTypeRegistry | 查询 | 获取 Order 类型配置 |

# 3 业务设计

## 3.1 提交模式对比

| 模式 | 触发方式 | 冲突检查 | 打断逻辑 | 排队逻辑 |
|------|----------|----------|----------|----------|
| **Immediate** | 普通按键 | 是 | TagRuleSet.RemovedTags | sameTypePolicy |
| **Queued** | Shift+按键 | 否 | 不打断 | 强制追加末尾 |

## 3.2 Immediate 模式流程

```
Order 到达 (Immediate)
    │
    ▼
┌───────────────────┐
│ 检查 BlockedTags  │ ──被阻止──→ 拒绝 (Blocked)
└─────────┬─────────┘
          │ 通过
          ▼
┌───────────────────┐
│ 检查 RemovedTags  │
│ (能否打断当前)    │
└─────────┬─────────┘
    能打断 │ 不能打断
    ┌──────┴──────┐
    ▼             ▼
┌─────────┐   ┌─────────────────────────┐
│ 打断当前 │   │ 按 sameTypePolicy 处理  │
│ 清空队列 │   │ ─────────────────────── │
│ 立即激活 │   │ queue:   入队           │
│ → Tag +  │   │ replace: 替换同类型     │
│   BB     │   │ ignore:  丢弃           │
└─────────┘   │                         │
              │ 还要考虑:               │
              │ - maxQueueSize          │
              │ - queueFullPolicy       │
              └─────────────────────────┘
```

## 3.3 Queued 模式流程 (Shift+)

```
Order 到达 (Queued / Shift+)
    │
    ▼
┌───────────────────┐
│ 检查 AllowShiftQ? │ ──否──→ 拒绝 (Ignored)
└─────────┬─────────┘
          │ 是
          ▼
┌───────────────────┐
│ 检查 ShiftQueue   │ ──满──→ 拒绝 (QueueFull)
│ MaxSize           │
└─────────┬─────────┘
          │ 未满
          ▼
┌───────────────────┐
│ 跳过冲突检查      │
│ 直接追加到队列末尾│
│ (FIFO)            │
└───────────────────┘
```

## 3.4 关键场景

### 场景 1：普通按键连按 (Immediate)

配置：`sameTypePolicy=Queue, maxQueueSize=3`

| 步骤 | 操作 | 结果 | OrderBuffer | Tag |
|------|------|------|-------------|-----|
| 1 | 按 Q | Activated | Active=Q1, Queued=[] | +Active.Cast, +HasActive |
| 2 | 按 Q | Queued | Active=Q1, Queued=[Q2] | +HasQueued |
| 3 | 按 Q | Queued | Active=Q1, Queued=[Q2,Q3] | - |
| 4 | 按 Q | Queued | Active=Q1, Queued=[Q3,Q4] | Q2 被丢弃 |

### 场景 2：Shift+ 强制排队 (Queued)

配置：`shiftQueueMaxSize=16`

| 步骤 | 操作 | 结果 | OrderBuffer |
|------|------|------|-------------|
| 1 | 右键移动 | Activated | Active=Move1 |
| 2 | Shift+右键 | Queued | Active=Move1, Queued=[Move2] |
| 3 | Shift+Q | Queued | Active=Move1, Queued=[Move2, Q1] |
| 4 | Shift+W | Queued | Active=Move1, Queued=[Move2, Q1, W1] |

关键：Shift+ 跳过冲突检查，所有命令按顺序排队执行。

### 场景 3：打断 (Immediate)

配置：CastAbility 的 RemovedTags 包含 MoveTo

| 步骤 | 操作 | 结果 | Tag |
|------|------|------|-----|
| 1 | 右键移动 | Activated | +Active.MoveTo |
| 2 | 按 Q | Activated (打断) | -Active.MoveTo, +Active.Cast |

### 场景 4：Order 完成后提升队列

| 步骤 | 事件 | OrderBuffer | Tag |
|------|------|-------------|-----|
| 1 | Q1 激活中 | Active=Q1, Queued=[Q2,Q3] | +HasActive, +HasQueued |
| 2 | Q1 完成 | Active=Q2, Queued=[Q3] | - |
| 3 | Q2 完成 | Active=Q3, Queued=[] | -HasQueued |
| 4 | Q3 完成 | Active=无, Queued=[] | -HasActive |

# 4 数据模型

## 4.1 Order 结构

```csharp
public struct Order
{
    public int OrderId;
    public int OrderTagId;
    public int PlayerId;
    public Entity Actor;
    public Entity Target;
    public Entity TargetContext;
    public OrderArgs Args;
    public int SubmitStep;
    public OrderSubmitMode SubmitMode;  // Immediate / Queued
}

public enum OrderSubmitMode : byte
{
    Immediate = 0,  // 普通模式：检查冲突，可能打断/排队
    Queued = 1      // Shift+ 模式：跳过冲突，强制追加
}
```

## 4.2 OrderTypeConfig 配置

```csharp
public class OrderTypeConfig
{
    public int OrderTagId { get; set; }
    public string Label { get; set; }
    
    // Immediate 模式配置
    public int MaxQueueSize { get; set; } = 3;
    public SameTypePolicy SameTypePolicy { get; set; } = SameTypePolicy.Queue;
    public QueueFullPolicy QueueFullPolicy { get; set; } = QueueFullPolicy.DropOldest;
    public int Priority { get; set; } = 100;
    public int BufferWindowMs { get; set; } = 500;
    public bool CanInterruptSelf { get; set; } = false;
    public int OrderStateTagId { get; set; }
    public bool AIBypassBuffer { get; set; } = false;
    
    // Queued 模式 (Shift+) 配置
    public int ShiftQueueMaxSize { get; set; } = 16;
    public bool AllowShiftQueue { get; set; } = true;
    
    // 打断配置
    public bool ClearQueueOnActivate { get; set; } = true;
}
```

## 4.3 Blackboard 组件

### BlackboardSpatialBuffer（位置/路径）

```csharp
public unsafe struct BlackboardSpatialBuffer
{
    public const int MAX_ENTRIES = 8;
    public const int MAX_POINTS_PER_ENTRY = 16;  // 支持路径点队列
    
    // 主要方法
    void SetPoint(int key, Vector3 point);        // 设置单点
    bool AppendPoint(int key, Vector3 point);     // 追加路径点
    bool PopFirstPoint(int key, out Vector3);     // 弹出第一个点（路径消耗）
    int GetPointCount(int key);                   // 获取路径点数量
}
```

### BlackboardEntityBuffer（Entity 引用）

```csharp
public unsafe struct BlackboardEntityBuffer
{
    public const int MAX_ENTRIES = 16;
    
    // 主要方法
    bool TryGet(int key, out Entity value);
    void Set(int key, Entity value);
    bool Remove(int key);
}
```

### Blackboard Key 常量

```csharp
public static class OrderBlackboardKeys
{
    // Move Order (100-109)
    public const int Move_Waypoints = 100;       // SpatialBuffer
    public const int Move_CurrentIndex = 101;    // IntBuffer
    public const int Move_FollowTarget = 102;    // EntityBuffer
    
    // Cast Ability Order (110-119)
    public const int Cast_SlotIndex = 110;       // IntBuffer
    public const int Cast_TargetEntity = 111;    // EntityBuffer
    public const int Cast_TargetPosition = 112;  // SpatialBuffer
    
    // Attack Order (120-129)
    public const int Attack_TargetEntity = 120;  // EntityBuffer
    public const int Attack_MovePosition = 121;  // SpatialBuffer
}
```

## 4.4 OrderState Tag 分配

| Tag ID | 名称 | 说明 |
|--------|------|------|
| 100 | Order.Active.CastAbility | 正在施法 |
| 101 | Order.Active.MoveTo | 正在移动 |
| 102 | Order.Active.AttackTarget | 正在攻击 |
| 103 | Order.Active.Stop | 正在停止 |
| 110 | Order.State.HasActive | 有 Order 执行中 |
| 111 | Order.State.HasQueued | 有 Order 排队中 |
| 112 | Order.State.Channeling | 正在引导 |
| 113 | Order.State.Casting | 正在施法 |
| 114 | Order.State.Interruptible | 可被打断 |

# 5 落地方式

## 5.1 模块划分与职责

| 模块 | 文件路径 | 职责 |
|------|----------|------|
| OrderBuffer | `Components/OrderBuffer.cs` | Order 队列数据结构 |
| OrderTypeConfig | `Orders/OrderTypeConfig.cs` | Order 类型配置 |
| OrderTypeRegistry | `Orders/OrderTypeRegistry.cs` | Order 类型注册表 |
| OrderStateTags | `Orders/OrderStateTags.cs` | Tag ID 常量 |
| OrderBlackboardKeys | `Orders/OrderBlackboardKeys.cs` | Blackboard Key 常量 |
| OrderSubmitter | `Orders/OrderSubmitter.cs` | 统一入口（核心） |
| BlackboardSpatialBuffer | `Components/BlackboardSpatialBuffer.cs` | 位置参数存储 |
| BlackboardEntityBuffer | `Components/BlackboardEntityBuffer.cs` | Entity 参数存储 |

## 5.2 关键接口

### OrderSubmitter（核心入口）

```csharp
public static class OrderSubmitter
{
    /// <summary>
    /// 提交 Order
    /// </summary>
    /// <param name="world">ECS World</param>
    /// <param name="entity">目标 Entity</param>
    /// <param name="order">Order 数据（含 SubmitMode）</param>
    /// <param name="registry">Order 类型配置</param>
    /// <param name="tagRuleRegistry">Tag 规则配置</param>
    /// <param name="currentStep">当前模拟步</param>
    /// <param name="isAI">是否来自 AI（影响 AIBypassBuffer）</param>
    /// <returns>提交结果</returns>
    public static OrderSubmitResult Submit(
        World world,
        Entity entity,
        in Order order,
        OrderTypeRegistry registry,
        TagRuleRegistry? tagRuleRegistry,
        int currentStep,
        bool isAI = false);
    
    /// <summary>
    /// 通知当前 Order 完成，提升下一个
    /// </summary>
    public static void NotifyOrderComplete(World world, Entity entity, OrderTypeRegistry registry);
    
    /// <summary>
    /// 取消当前 Order（但保留队列）
    /// </summary>
    public static void CancelCurrent(World world, Entity entity, OrderTypeRegistry registry);
    
    /// <summary>
    /// 取消所有 Order（清空队列）
    /// </summary>
    public static void CancelAll(World world, Entity entity);
    
    /// <summary>
    /// 追加路径点（Shift+右键行为）
    /// </summary>
    public static bool AppendWaypoint(World world, Entity entity, Vector3 point);
}

public enum OrderSubmitResult
{
    Activated = 0,      // 立即激活（→ Tag + Blackboard）
    Queued = 1,         // 入队等待
    Blocked = 2,        // 被 BlockedTags 阻止
    QueueFull = 3,      // 队列已满
    Ignored = 4,        // 被 sameTypePolicy=Ignore 丢弃
    DirectExecuted = 5, // AI 直接执行（跳过 Buffer）
    InvalidEntity = 6   // Entity 无效或缺少组件
}
```

## 5.3 JSON 配置示例

### order_types.json

```json
{
  "orderTypes": {
    "castAbility": {
      "orderTagId": 100,
      "label": "Cast Ability",
      "maxQueueSize": 3,
      "sameTypePolicy": "Queue",
      "queueFullPolicy": "DropOldest",
      "priority": 100,
      "bufferWindowMs": 500,
      "orderStateTagId": 100,
      "shiftQueueMaxSize": 8,
      "allowShiftQueue": true,
      "clearQueueOnActivate": true
    },
    "moveTo": {
      "orderTagId": 101,
      "label": "Move To",
      "maxQueueSize": 1,
      "sameTypePolicy": "Replace",
      "priority": 50,
      "orderStateTagId": 101,
      "shiftQueueMaxSize": 16,
      "allowShiftQueue": true,
      "clearQueueOnActivate": true
    }
  }
}
```

### input_order_mappings.json

```json
{
  "mappings": [
    {
      "actionId": "SkillQ",
      "trigger": "PressedThisFrame",
      "orderTagKey": "castAbility",
      "argsTemplate": { "i0": 0 },
      "modifierBehavior": "ShiftToQueue"
    },
    {
      "actionId": "Command",
      "trigger": "PressedThisFrame",
      "orderTagKey": "moveTo",
      "requireSelection": true,
      "selectionType": "Ground",
      "modifierBehavior": "ShiftToQueue"
    }
  ]
}
```

# 6 与其他模块的职责切分

| 职责 | 负责模块 |
|------|----------|
| Input → Order 转换 | InputOrderMappingSystem |
| Order 缓冲与调度 | OrderSubmitter |
| Order 执行状态 | GameplayTagContainer |
| Order 参数存储 | Blackboard*Buffer 组件 |
| Order 执行 | AbilityTimeline / MoveSystem |
| 冲突规则定义 | TagRuleRegistry |
| Order 类型配置 | OrderTypeRegistry |

# 7 执行系统集成示例

执行系统通过 Query Tag + Blackboard 获取数据：

```csharp
public class MovementSystem : BaseSystem<World, float>
{
    public override void Update(in float dt)
    {
        var query = World.Query<GameplayTagContainer, BlackboardSpatialBuffer, Transform>();
        
        foreach (var chunk in query)
        {
            foreach (var entity in chunk)
            {
                ref var tags = ref entity.Get<GameplayTagContainer>();
                
                // 检查是否有移动 Order 激活
                if (!tags.HasTag(OrderStateTags.Active_MoveTo)) continue;
                
                ref var spatial = ref entity.Get<BlackboardSpatialBuffer>();
                ref var transform = ref entity.Get<Transform>();
                
                // 获取当前目标点
                if (!spatial.TryGetPoint(OrderBlackboardKeys.Move_Waypoints, out var targetPos))
                {
                    // 无路径点，移动完成
                    OrderSubmitter.NotifyOrderComplete(World, entity, _registry);
                    continue;
                }
                
                // 移动逻辑
                var direction = targetPos - transform.Position;
                if (direction.LengthSquared() < 0.01f)
                {
                    // 到达当前点，消耗路径点
                    spatial.PopFirstPoint(OrderBlackboardKeys.Move_Waypoints, out _);
                    
                    // 检查是否还有更多路径点
                    if (spatial.GetPointCount(OrderBlackboardKeys.Move_Waypoints) == 0)
                    {
                        // 路径完成
                        OrderSubmitter.NotifyOrderComplete(World, entity, _registry);
                    }
                }
                else
                {
                    // 继续移动
                    transform.Position += Vector3.Normalize(direction) * _speed * dt;
                }
            }
        }
    }
}
```

# 8 验收条款

1. **Immediate 模式**：普通按键触发，根据 TagRuleSet 和 sameTypePolicy 处理
   - 证据：OrderBuffer 状态符合预期

2. **Queued 模式**：Shift+ 按键触发，跳过冲突检查，直接追加队列
   - 证据：所有 Shift+ 命令按顺序进入队列

3. **Tag 状态同步**：Order 激活/完成后，对应 Tag 正确添加/移除
   - 证据：HasActive / HasQueued Tag 状态正确

4. **Blackboard 参数**：Order 参数正确写入 Blackboard 组件
   - 证据：执行系统可从 Blackboard 读取参数

5. **路径点支持**：Shift+右键可追加路径点到 BlackboardSpatialBuffer
   - 证据：路径点队列正确累积

6. **AI 等价性**：AI 调用 OrderSubmitter 与玩家效果等价
   - 证据：AI 提交的 Order 经过相同处理流程
