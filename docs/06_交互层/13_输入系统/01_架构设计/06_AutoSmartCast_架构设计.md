---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v0.1
适用范围: Auto SmartCast 最近目标回退
状态: 设计中
---
# Auto SmartCast 最近目标回退 — 架构设计

## 1 动机

SmartCast 模式下，若无 hovered entity，当前系统回退到 selected entity 或直接失败。ARPG/MOBA 场景需要自动选取范围内最近有效目标。

## 2 设计

### 2.1 AutoTargetPolicy

在 `InputOrderMapping` 上添加策略字段：

```csharp
public enum AutoTargetPolicy
{
    None = 0,           // 无自动目标（当前行为）
    NearestEnemy = 1,   // 最近敌方
    NearestAlly = 2,    // 最近友方
    NearestAny = 3      // 最近任意
}
```

### 2.2 触发条件

仅在以下条件全部满足时触发：
1. `InteractionMode == SmartCast` 或 `SmartCastWithIndicator`
2. `SelectionType == Entity`
3. `_hoveredEntityProvider` 返回 false（无悬停实体）
4. `AutoTargetPolicy != None`

### 2.3 查询方式

通过 `ISpatialQueryService` 执行最近目标查询：

```csharp
// InputOrderMappingSystem 中新增回调
public delegate bool AutoTargetQueryProvider(
    Entity actor, AutoTargetPolicy policy, float range, out Entity target);
```

查询范围从 `AbilityDefinition.Range` 或 Mapping 配置中读取。

### 2.4 配置

```json
{
  "actionId": "SkillQ",
  "trigger": "PressedThisFrame",
  "orderTagKey": "castAbility",
  "selectionType": "Entity",
  "isSkillMapping": true,
  "autoTargetPolicy": "NearestEnemy",
  "autoTargetRange": 500
}
```
