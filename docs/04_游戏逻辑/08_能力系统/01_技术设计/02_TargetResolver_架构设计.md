---
文档类型: 架构设计
创建日期: 2026-02-06
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v0.2
适用范围: 游戏逻辑 - 能力系统 - TargetResolver
状态: 已实现（待重构）
依赖文档:
  - docs/03_基础服务/06_空间服务/01_架构设计/01_空间服务_架构设计.md
  - docs/04_游戏逻辑/08_能力系统/02_统一规范/01_GAS_设计总览.md
---

# TargetResolver 架构设计

# 1 设计概述

## 1.1 本文档定义

本文档定义 TargetResolver（效果扇出解析器）的职责边界、数据模型、过滤链路与预算保护机制。TargetResolver 负责将一个效果（Effect）从单目标扩展为多目标扇出，支撑 AOE、锥形打击、波浪范围等技能形态。

## 1.2 设计目标

1. 可组合：形状/过滤/映射可配置驱动，新增形状无需改 System 代码。
2. 可审计：预算保护 + dropped 可观测，防止扇出引发无限递归。
3. 无重复：扇出逻辑集中在 `TargetResolverFanOutHelper`，两个 System（Apply / Lifetime）共享。

# 2 核心数据模型

## 2.1 TargetResolverDescriptor

```csharp
public struct TargetResolverDescriptor
{
    public TargetResolverKind Kind;          // None / BuiltinSpatial / GraphProgram
    public BuiltinSpatialDescriptor Spatial; // 空间查询参数
    public int GraphProgramId;               // Graph VM 程序 ID
    public int PayloadEffectTemplateId;      // 应用到每个目标的效果模板
    public TargetResolverContextMapping ContextMapping; // 上下文映射
}
```

## 2.2 BuiltinSpatialDescriptor

| 字段 | 说明 |
|---|---|
| Shape | Circle / Cone / Rectangle / Line / Ring |
| RadiusCm | 查询半径（圆/锥/环外径） |
| InnerRadiusCm | 环内径 |
| HalfAngleDeg | 锥形半角 |
| HalfWidthCm / HalfHeightCm | 矩形半宽/半高 |
| LengthCm | 线段长度 |
| RelationFilter | 关系过滤（All / Hostile / Friendly / Neutral / NotFriendly / NotHostile） |
| LayerMask | 层级位掩码过滤 |
| ExcludeSource | 是否排除施法者 |
| MaxTargets | 目标上限（0 = 仅受预算限制） |

## 2.3 ContextMapping

配置 payload EffectRequest 中 Source/Target/TargetContext 如何从原始上下文 + 解析结果映射：

| 预设 | PayloadSource | PayloadTarget | PayloadTargetContext |
|---|---|---|---|
| AOE（默认） | OriginalSource | ResolvedEntity | OriginalTarget |
| Reflect | OriginalTarget | OriginalSource | OriginalTarget |
| Redirect | OriginalSource | OriginalTargetContext | OriginalTarget |

# 3 过滤链路

```
空间查询（形状分派）
  → ExcludeSource（排除施法者）
  → Ring 内径排除
  → LayerMask 位掩码 AND（EntityLayer.Category）
  → RelationshipFilter（TeamManager 配置驱动）
  → 预算 TryConsume（RootBudgetTable）
  → 收集 FanOutCommand
```

每个步骤都是 short-circuit：不通过则 `continue`，避免后续查询成本。

# 4 共享 Helper

## 4.1 TargetResolverFanOutHelper

`src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs` 提供以下静态方法：

| 方法 | 职责 |
|---|---|
| `CollectFanOutTargets(...)` | 完整扇出收集链路（中心解析→方向计算→形状查询→过滤→预算→收集） |
| `PublishFanOutCommands(...)` | 将收集的 FanOutCommand 列表发布为 EffectRequest |
| `ResolveSlot(...)` | 根据 ContextSlot 映射获取 Entity |

调用方：

- `EffectApplicationSystem`（on-apply 扇出）
- `EffectLifetimeSystem`（periodic 扇出）

## 4.2 FanOutCommand

```csharp
public struct FanOutCommand
{
    public int RootId;
    public Entity OriginalSource;
    public Entity OriginalTarget;
    public Entity OriginalTargetContext;
    public int PayloadEffectTemplateId;
    public TargetResolverContextMapping ContextMapping;
    public Entity ResolvedEntity;
}
```

# 5 时间切片

`EffectApplicationSystem` 的 Stage 5（fan-out publish）已支持时间切片：

- 使用 `_playbackCursor` + `workUnits` 循环
- 与 Stage 1-4 行为一致：超过 `MaxWorkUnitsPerSlice` 时中断并在下帧继续

# 6 确定性

方向计算使用 `Fix64Math.Atan2Fast`（定点数），空间查询中的 `SinCosDeg` 使用 `MathUtil.Sin/Cos`（查表法），保证帧同步场景下的确定性。

# 7 性能优化

`EffectTemplateRegistry` 提供 `TryGetRef` / `GetRef` 方法返回 `ref readonly EffectTemplateData`，扇出路径使用 ref 版本避免 >100 字节的 struct 拷贝。

# 8 代码入口（文件路径）

| 文件 | 职责 |
|---|---|
| `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs` | 共享扇出收集/发布逻辑 |
| `src/Core/Gameplay/GAS/EffectTemplateRegistry.cs` | 数据模型 + ref readonly 访问 |
| `src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs` | on-apply 扇出调用方 |
| `src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs` | periodic 扇出调用方 |

# 9 架构演进：三层拆分

> **本节描述已决定但尚未落地的架构改造**，见 `11_Effect改造清单_架构设计.md` #8。

当前 `TargetResolverDescriptor` 是一个单体 struct，将"查"（空间/Graph 查询）、"筛"（关系/层级过滤）、"做"（对每个目标施加 payload effect）耦合在一起。

改造方向：拆为三个独立 struct，对齐运行时已有的三阶段执行（`ResolveTargets` → `ValidateAndCollect` → `PublishFanOutCommands`）：

| 新 struct | 对应 JSON | 职责 |
|---|---|---|
| `TargetQueryDescriptor` | `targetQuery` | 填 target list（空间查询 / Graph / 未来: Relationship / HexAdjacent） |
| `TargetFilterDescriptor` | `targetFilter` | 验证 candidate（关系 / 层级 / 数量上限） |
| `TargetDispatchDescriptor` | `targetDispatch` | 对每个 target 发 payload effect + 上下文映射 |

**影响**：
- `EffectTemplateRegistry` 中 `TargetResolverDescriptor` 字段拆为三个独立字段
- `TargetResolverFanOutHelper` 方法签名更新为接受三个独立 descriptor
- JSON 从扁平 `targetResolver` 改为 `targetQuery` + `targetFilter` + `targetDispatch`
- 查询参数注入 `_ep.*` ConfigParams 键，CallerParams 可覆盖（如半径、角度等）

详细设计见：
- 类型体系中的三层拆分 → `04_Effect类型体系_架构设计.md` 第 3.2.1 节
- 参数注入 → `05_Effect参数架构_架构设计.md` 第 2.2 节
- JSON 配置 → `03_配置结构/01_EffectPresetType定义_配置结构.md` 第 4.4-4.6 节
- EffectTemplate JSON → `03_配置结构/02_EffectTemplate配置_配置结构.md` 第 3.4-3.6 节
