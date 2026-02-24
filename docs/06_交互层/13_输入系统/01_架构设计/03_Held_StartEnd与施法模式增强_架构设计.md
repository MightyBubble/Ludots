---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v1.0
适用范围: InputOrderMapping 体系增强
状态: 已实现
---
# Held Start/End 与施法模式增强 — 架构设计

## 1 概述

本文档记录对 `InputOrderMapping` 体系的三项增强：
1. Held → Start/End Order 映射（HeldPolicy）
2. SmartCastWithIndicator 交互模式
3. OrderSelectionType 重命名与新增类型

## 2 Held → Start/End Order 映射

### 2.1 动机

原始 `Held` 触发器每帧发射 Order，对于"按住蓄力→松开释放"或"按住开启→松开关闭"类技能不适用。需要在 Input 层优雅地解耦为 Start/End 两个 Order。

### 2.2 设计

新增 `HeldPolicy` 枚举：

```csharp
public enum HeldPolicy
{
    EveryFrame = 0,  // 每帧触发（现有行为）
    StartEnd = 1     // 按下发射 .Start，松开发射 .End
}
```

`InputOrderMapping` 新增 `HeldPolicy` 字段，仅当 `Trigger == Held` 时有意义。

### 2.3 运行时行为

- `StartEnd` 模式下，系统追踪 `_activeHeldStartEndActions` 集合
- PressedThisFrame → 调用 `_tagKeyResolver(orderTagKey + ".Start")` → 提交 Start Order
- ReleasedThisFrame → 调用 `_tagKeyResolver(orderTagKey + ".End")` → 提交 End Order
- 释放检测在 `ProcessHeldStartEndReleases()` 中执行，位于 Update 最前端（确保即使在 aiming 状态也不漏 release）

### 2.4 配置示例

```json
{
  "actionId": "SkillQ",
  "trigger": "Held",
  "heldPolicy": "StartEnd",
  "orderTagKey": "castAbility",
  "argsTemplate": { "i0": 0 },
  "isSkillMapping": true
}
```

Mod 的 TagKeyResolver 需注册 `"castAbility.Start"` 和 `"castAbility.End"` 两个 Order Tag。

## 3 SmartCastWithIndicator 交互模式

### 3.1 动机

LoL "Quick Cast with Indicator" 模式：按住技能键显示指示器，松开释放。这是介于 SmartCast（即按即放）和 AimCast（按键进入瞄准→点击确认）之间的第四种模式。

### 3.2 设计

```csharp
public enum InteractionModeType
{
    TargetFirst = 0,
    SmartCast = 1,
    AimCast = 2,
    SmartCastWithIndicator = 3  // 新增
}
```

### 3.3 运行时行为

1. PressedThisFrame（技能键） → 进入 aiming 状态 → 触发 `AimingStateChanged(true, mapping)` → Performer 显示指示器
2. 每帧 → 触发 `AimingUpdateHandler` → Performer 更新指示器位置
3. ReleasedThisFrame（同一技能键） → 提交 Order（SmartCast 逻辑） → 退出 aiming → 触发 `AimingStateChanged(false, mapping)`
4. 右键/ESC → 取消 → 退出 aiming

### 3.4 Performer 配合

当前 Performer 位置绑定 Owner entity。要实现光标跟随指示器，需要：
- **方案 A（推荐）**：创建光标追踪辅助 Entity，每帧更新其 `VisualTransform` 为光标世界坐标
- **方案 B**：在 `PerformerEmitSystem` 增加 `PositionOverride` 参数

`AimingUpdateHandler` 回调已在系统中 wire，消费者需实现实际位置更新逻辑。

## 4 OrderSelectionType 增强

### 4.1 变更

| 旧值 | 新值 | 说明 |
|---|---|---|
| `Ground = 1` | `Position = 1` | 重命名，更准确地表达"世界坐标位置" |
| - | `Direction = 4` | 二维方向向量（锥形/线形技能方向） |
| - | `Vector = 5` | 两点输入（起点+终点，向量定向技能） |

`Ground` 保留为 `[Obsolete]` 别名（`= Position`），保证向后兼容。

### 4.2 Direction 数据流

Direction 类型的 Order 在 `InputOrderMappingSystem` 中将光标位置存入 `OrderSpatial.WorldCm`。下游系统可从 actor 位置和此位置计算方向向量。

### 4.3 Vector 数据流（待实现）

Vector 类型需要两阶段输入状态机：
1. Press → 记录起点
2. Drag → 实时更新方向
3. Release → 记录终点，`OrderSpatial` 存储 [起点, 终点]

此状态机尚未实现，需要新增 `VectorAimState` 跟踪。

## 5 涉及文件

- `src/Core/Input/Orders/InputOrderMapping.cs` — 枚举和数据模型
- `src/Core/Input/Orders/InputOrderMappingSystem.cs` — 运行时逻辑
- `src/Core/Gameplay/GAS/Components/TargetSelector.cs` — TargetShape 新增 Line/Ring/Rectangle
