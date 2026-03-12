# S7: Shift 队列（Shift Queue）

> 按住 Shift 键发出指令时，指令加入队列顺序执行，而非立即覆盖当前指令。如：星际争霸 Shift+右键排队移动。

---

## 机制描述

玩家按住 Shift 键的同时发出指令，指令追加到队列末尾，当前指令完成后按顺序执行。不按 Shift 时，新指令立即覆盖队列中的所有待执行指令。常见于：
- **星际争霸（StarCraft）**：Shift+右键排队移动、Shift+技能排队施放
- **魔兽争霸 3（Warcraft 3）**：Shift+指令队列化
- **帝国时代（Age of Empires）**：Shift+移动点队列移动

与 S1（组合键）的区别：S7 是指令提交模式（队列/立即），S1 是不同技能 ID。

---

## 交互层设计

- **Trigger**: `PressedThisFrame`
- **SelectionType**: 根据技能需求
- **InteractionMode**: 根据技能需求
- **ModifierSubmitBehavior**: `QueueOnModifier`

```json5
// 配置路径: mods/<yourMod>/InputOrderMappings/move_order.json
// 接口定义: src/Core/Input/Orders/InputOrderMapping.cs
{
  "actionId": "move_to_point",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "move",
  "selectionType": "Position",
  "isSkillMapping": false,
  "modifierSubmitBehavior": "QueueOnModifier",    // Shift 按住时 = Queued
  "modifierKey": "Shift"                          // 修饰键为 Shift
}
```

**OrderBuffer 行为**：
```
Shift 未按时: Order.SubmitMode = Immediate → 清空队列并立即执行
Shift 按住时: Order.SubmitMode = Queued   → 追加到队列末尾
```

---

## Graph 实现

Shift 队列不改变技能的 Graph 实现，差异仅在指令提交模式。

```
// 队列管理由 OrderBuffer 处理，与 Graph 实现无关
// Graph op 参考: src/Core/NodeLibraries/GASGraph/GraphOps.cs
// Effect 模板:  mods/<yourMod>/Effects/move_order_main.json
// 注册中心:     src/Core/Gameplay/GAS/EffectTemplateRegistry.cs

// 移动指令无 Effect，直接由 MovementSystem 处理
// 技能队列化：技能 Graph 与普通施放完全相同
Phase OnApply:
  LoadContextSource         E[0]
  LoadContextTarget         E[1]
  // ... 技能效果实现
```

**OrderBuffer 数据结构**：
```csharp
// src/Core/Input/Orders/OrderBuffer.cs
public struct OrderBuffer
{
    public Order ImmediateOrder;                // 立即执行的指令
    public FixedList32<Order> QueuedOrders;     // 队列化指令（最多 32 个）
}
```

---

## 依赖基建

| 组件 | 路径 | 状态 |
|------|------|------|
| InputOrderMapping | `src/Core/Input/Orders/InputOrderMapping.cs` | ✅ 已有（ModifierSubmitBehavior 字段） |
| OrderBuffer | `src/Core/Input/Orders/OrderBuffer.cs` | ✅ 已有（QueuedOrders 队列） |
| InputOrderMappingSystem | `src/Core/Input/Orders/InputOrderMappingSystem.cs` | ✅ 已有（队列提交逻辑） |

---

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 无 | - | 现有基建可完整表达。`InputOrderMapping.modifierSubmitBehavior = QueueOnModifier` 已支持，`OrderBuffer.QueuedOrders` 已实现队列管理。 |

---

## 最佳实践

- **DO**: 在 UI 上显示排队中的指令图标（按顺序显示队列内容），帮助玩家了解队列状态。
- **DO**: 限制队列最大长度（如 32 个），避免玩家意外积累过长队列。
- **DO**: 为取消队列提供快捷键（如 Shift+Stop 或 Esc 清空队列）。
- **DON'T**: 不要在 Graph 层实现队列逻辑（属于 Input/Order 层职责）。
- **DON'T**: 不要将 Shift 队列与 S1（Shift 组合键）冲突：需要在 InputBackend 层区分 Shift+Q（组合键技能）和 Shift+右键（队列移动）。

---

## 验收口径

### 场景 1: Shift 队列移动

| 项 | 内容 |
|----|------|
| **输入** | 玩家右键移动到 A 点，然后按 Shift+右键移动到 B 点、C 点 |
| **预期输出** | 单位依次移动：A → B → C，到达 A 后自动开始移动到 B |
| **Log 关键字** | `[Input] OrderSubmitted move A SubmitMode=Immediate` <br> `[Input] OrderQueued move B QueuedOrders=1` <br> `[Input] OrderQueued move C QueuedOrders=2` <br> `[Order] OrderCompleted move A, dequeue next=B` |
| **截图要求** | 单位路径线上显示 A→B→C 的路径标记，依次到达各点 |
| **多帧录屏** | F0: 移动到 A → F50: 到达 A → F51: 自动移动 B → F100: 到达 B → F101: 自动移动 C |

### 场景 2: 无 Shift（立即覆盖）

| 项 | 内容 |
|----|------|
| **输入** | 单位正在移动到 A，玩家直接右键移动到 B（不按 Shift） |
| **预期输出** | 单位立即转向移动到 B，放弃 A 目标 |
| **Log 关键字** | `[Input] OrderSubmitted move B SubmitMode=Immediate` <br> `[Order] QueueCleared by Immediate order` |
| **截图要求** | 单位立即改变方向，路径线变为 → B |
| **多帧录屏** | F0-F25: 移动向 A → F26: 新移动指令 → F27: 立即转向 B |

### 场景 3: 队列技能施放

| 项 | 内容 |
|----|------|
| **输入** | 按 Q 技能，然后 Shift+W 技能，Shift+E 技能 |
| **预期输出** | 依次施放 Q → W → E，每个技能完成后自动施放下一个 |
| **Log 关键字** | `[Input] OrderSubmitted Q SubmitMode=Immediate` <br> `[Input] OrderQueued W QueuedOrders=1` <br> `[Input] OrderQueued E QueuedOrders=2` |
| **截图要求** | 技能施放动画依次播放，无中断 |

---

## 测试用例设计

### 单元测试
```csharp
// 路径: src/Tests/GasTests/SpecialInput/ShiftQueueTests.cs
[Test]
public void S7_ShiftHeld_AddsToQueue()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0));

    // Act
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(1000, 0));  // 立即执行
    world.HoldKey(source, "Shift");
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(2000, 0));  // 队列
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(3000, 0));  // 队列

    // Assert
    var buffer = GetOrderBuffer(world, source);
    Assert.AreEqual(1000, buffer.ImmediateOrder.Target.x);
    Assert.AreEqual(2, buffer.QueuedOrders.Length);
    Assert.AreEqual(2000, buffer.QueuedOrders[0].Target.x);
    Assert.AreEqual(3000, buffer.QueuedOrders[1].Target.x);
}

[Test]
public void S7_ImmediateOrder_ClearsQueue()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0));
    world.HoldKey(source, "Shift");
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(1000, 0));
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(2000, 0));
    world.ReleaseKey(source, "Shift");

    // Act
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(5000, 0));  // 立即执行

    // Assert
    var buffer = GetOrderBuffer(world, source);
    Assert.AreEqual(5000, buffer.ImmediateOrder.Target.x);
    Assert.AreEqual(0, buffer.QueuedOrders.Length);  // 队列已清空
}

[Test]
public void S7_QueueAutoDequeue_AfterCompletion()
{
    // Arrange
    var world = CreateTestWorld();
    var source = SpawnUnit(world, position: (0, 0), moveSpeed: 100);
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(100, 0));  // 1 秒到达
    world.HoldKey(source, "Shift");
    world.SubmitOrder(source, OrderType.Move, spatial: new Vector2(200, 0));

    // Act
    world.Tick(60);  // 1 秒 = 到达第一个目标

    // Assert
    var pos = GetPosition(world, source);
    Assert.Greater(pos.x, 90);  // 已到达或接近第一个目标
    var buffer = GetOrderBuffer(world, source);
    Assert.AreEqual(200, buffer.ImmediateOrder.Target.x);  // 已出队第二个指令
}
```

### 集成验收
1. 运行 `dotnet test --filter "S7"` — 全绿。
2. 在 Playground 中录制 60 帧日志，存档：
   - `artifacts/acceptance/special_input/s7_shift_queue_trace.jsonl`
3. 截图存档：
   - `artifacts/acceptance/special_input/s7_queue.png`（Shift 队列）
   - `artifacts/acceptance/special_input/s7_immediate.png`（立即覆盖）
4. 多帧录屏：
   - `artifacts/acceptance/special_input/s7_60frames.gif`
   - 标注关键帧：队列添加、第一个到达、自动出队、第二个开始

---

## 参考案例

- **星际争霸（StarCraft）排队指令**: Shift+右键排队移动，Shift+技能排队施放，是高阶操作的基础。
- **魔兽争霸 3（Warcraft 3）指令队列**: Shift+指令追加到队列，完成后自动执行下一个。
- **帝国时代（Age of Empires）路径排队**: Shift+移动点设置多个路径节点，单位依次到达。
