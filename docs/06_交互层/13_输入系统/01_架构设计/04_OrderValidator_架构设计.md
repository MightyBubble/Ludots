---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v0.1
适用范围: 客户端 Order 合法性验证
状态: 设计中
---
# 客户端 Order 合法性验证器 — 架构设计

## 1 动机

参考 SC2，Input/Order 提交前需在客户端侧进行合法性验证（战争迷雾可见性、NavMesh 可达性、施法距离等），防止无效 Order 进入系统。建筑放置也复用此验证管线。

## 2 接口设计

```csharp
public enum OrderValidationResult : byte
{
    Valid = 0,
    TargetOutOfRange = 1,
    TargetNotVisible = 2,
    PositionUnreachable = 3,
    PositionBlocked = 4,
    Custom = 255
}

public interface IOrderValidator
{
    OrderValidationResult Validate(in Order order, Entity actor, World world);
}
```

## 3 验证链

```
OrderSubmitter.Submit()
  → IOrderValidator[0].Validate()  (RangeValidator)
  → IOrderValidator[1].Validate()  (NavMeshReachabilityValidator)
  → IOrderValidator[2].Validate()  (FogOfWarValidator)
  → ...
  → 全部 Valid → 继续执行原有逻辑
  → 任一失败 → 返回 OrderSubmitResult.ValidationFailed
```

验证器链在 `OrderSubmitter` 中作为可选参数传入，不影响现有 API。

## 4 内置验证器

| 验证器 | 职责 | 依赖 |
|---|---|---|
| `RangeValidator` | 目标是否在施法距离内 | `AbilityDefinition.Range` |
| `NavMeshReachabilityValidator` | 目标位置是否在 NavMesh 上 | `INavMeshService` |
| `FogOfWarValidator` | 目标实体/位置是否在己方视野内 | `IFogOfWarService` |
| `PlacementValidator` | 建筑放置位置是否合法（碰撞/地形） | `ISpatialQueryService` |

## 5 Graph Function 扩展点

验证逻辑可通过 Graph 节点扩展，允许 Mod 自定义验证规则：

```json
{
  "orderType": "buildStructure",
  "validationGraph": "graphs/build_placement_validation.json"
}
```

## 6 建筑放置复用

建筑放置 = 特殊 Order（`OrderTagKey: "buildStructure"`）+ Position 选择 + 幽灵预览 + PlacementValidator。

复用 AimCast 状态机显示放置预览，复用验证器链检查位置合法性。
