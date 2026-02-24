---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 参数架构（三层参数空间 + CallerParams + Merge）
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
  - docs/04_游戏逻辑/08_能力系统/03_配置结构/02_EffectTemplate配置_配置结构.md
---

# Effect 参数架构 架构设计

# 1 三层参数空间

```text
┌─────────────────────────────────────────────────┐
│  Layer 3: CallerParams（调用者级覆盖）            │
│  来源: 任何构造 EffectRequest 的系统              │
│        （AbilityTimeline, BuffSystem, 等）       │
│  存储: EffectRequest.CallerParams                │
│        + EffectCallerParams 组件 (per-instance)  │
│  语义: caller wins, 覆盖同 key 的下层值          │
├─────────────────────────────────────────────────┤
│  Layer 2: ConfigParams（模板级自定义参数）        │
│  来源: effects.json 的 configParams 字段         │
│  存储: EffectTemplateData.ConfigParams           │
│  语义: 模板共享，所有实例读到相同值               │
├─────────────────────────────────────────────────┤
│  Layer 1: 组件参数（由 PresetType 声明的参数组件）│
│  来源: effects.json 的组件相关字段               │
│        由 PresetType 定义表决定该类型需要哪些组件 │
│  存储: 编译时注入 ConfigParams（_ep.* 前缀）     │
│  语义: 类型决定的默认参数                        │
└─────────────────────────────────────────────────┘
```

**核心链路**：PresetType 定义表声明组件 → 编译时按组件集注入 `_ep.*` 键 → Graph 程序通过 `LoadConfigFloat/Int` 统一读取 → CallerParams 可覆盖任何 key。

**读取时机**：Graph 程序通过 `LoadConfigFloat/Int` 统一读取合并后的值，不区分参数来自哪一层、哪个组件。

# 2 结构参数与 ConfigParams 的统一

## 2.1 EffectParamKeys —— effect 系统拥有的保留键

```csharp
// 新增文件: src/Core/Gameplay/GAS/EffectParamKeys.cs
public static class EffectParamKeys
{
    // ── DurationParams 组件 ──
    public static int DurationTicks;
    public static int PeriodTicks;
    public static int ClockId;

    // ── TargetQueryParams 组件（查 —— 填 target list 的策略参数）──
    //    按 QueryStrategy 子类型分组，只注入实际用到的 key
    public static int QueryRadius;
    public static int QueryInnerRadius;    // Ring
    public static int QueryHalfAngle;      // Cone
    public static int QueryHalfWidth;      // Rectangle, Line
    public static int QueryHalfHeight;     // Rectangle
    public static int QueryLength;         // Line
    public static int QueryRotation;       // Rectangle
    // 未来: public static int QueryHexRange;       // HexAdjacent
    // 未来: public static int QueryRelationType;   // Relationship

    // ── TargetFilterParams 组件（筛 —— 验证 candidate）──
    public static int FilterMaxTargets;
    // RelationFilter, ExcludeSource, LayerMask 当前为 enum/bool/uint，
    // 暂不注入 ConfigParams（不太适合 float/int key），保持结构字段。
    // 未来如需 CallerParams 覆盖，可按需增加。

    // ── TargetDispatchParams 组件（做 —— 对每个 target 执行）──
    public static int DispatchPayloadEffectId;
    // ContextMapping 是结构体（三个 enum），不适合拆为多个 int key，保持结构字段。

    // ── ForceParams 组件 ──
    public static int ForceXAttribute;
    public static int ForceYAttribute;

    // ── 未来组件示意 ──
    // public static int ProjectileSpeed;     // ProjectileParams
    // public static int UnitTypeId;          // UnitCreationParams

    public static void Initialize()
    {
        // DurationParams
        DurationTicks       = TagRegistry.Register("_ep.durationTicks");
        PeriodTicks         = TagRegistry.Register("_ep.periodTicks");
        ClockId             = TagRegistry.Register("_ep.clockId");

        // TargetQueryParams（按策略子类型选择性注入）
        QueryRadius         = TagRegistry.Register("_ep.queryRadius");
        QueryInnerRadius    = TagRegistry.Register("_ep.queryInnerRadius");
        QueryHalfAngle      = TagRegistry.Register("_ep.queryHalfAngle");
        QueryHalfWidth      = TagRegistry.Register("_ep.queryHalfWidth");
        QueryHalfHeight     = TagRegistry.Register("_ep.queryHalfHeight");
        QueryLength         = TagRegistry.Register("_ep.queryLength");
        QueryRotation       = TagRegistry.Register("_ep.queryRotation");

        // TargetFilterParams
        FilterMaxTargets    = TagRegistry.Register("_ep.filterMaxTargets");

        // TargetDispatchParams
        DispatchPayloadEffectId = TagRegistry.Register("_ep.dispatchPayloadEffectId");

        // ForceParams
        ForceXAttribute     = TagRegistry.Register("_ep.forceXAttribute");
        ForceYAttribute     = TagRegistry.Register("_ep.forceYAttribute");
    }
}
```

**命名约定**：保留键以 `_ep.` 前缀（effect param），区分用户自定义 key。这些 key 由 effect 系统**自身**定义和拥有，外部系统（timeline、buff 等）只是通过 CallerParams 填值，不知道也不需要知道内部消费逻辑。

**与组件的对应**：每个参数组件的所有字段都有对应的 `_ep.*` key，在代码中按组件分组注释。新增组件时在此文件追加即可。

**务实原则**：`RelationFilter`（enum）、`ExcludeSource`（bool）、`LayerMask`（uint）、`ContextMapping`（struct）这些非数值型字段暂不注入 ConfigParams——它们不适合 int/float key 表示。如果未来确实需要 CallerParams 覆盖，再按需添加 key 和解析逻辑。

## 2.2 编译时注入策略 —— 由类型定义表的组件声明驱动

`EffectTemplateLoader.Compile()` 根据 PresetType 定义表声明的**参数组件集**，决定注入哪些 `_ep.*` 键。不再是 ad-hoc 的 if/switch，而是**类型定义表驱动**的：

```csharp
// 概念示意：根据类型定义表的组件声明注入
var typeDef = PresetTypeRegistry.Get(template.PresetType);

// ── DurationParams 组件 ──
if (typeDef.HasComponent(ComponentFlags.DurationParams))
{
    configParams.TryAddInt(EffectParamKeys.DurationTicks, durationTicks);
    configParams.TryAddInt(EffectParamKeys.PeriodTicks, periodTicks);
}

// ── TargetQueryParams 组件（查）──
//    按 QueryStrategy 子类型细化，只注入该策略需要的 key
if (typeDef.HasComponent(ComponentFlags.TargetQueryParams))
{
    switch (queryStrategy)
    {
        case QueryStrategy.BuiltinSpatial:
            var spatial = resolverDescriptor.Spatial;
            switch (spatial.Shape)
            {
                case SpatialShape.Circle:
                    configParams.TryAddInt(EffectParamKeys.QueryRadius, spatial.RadiusCm);
                    break;
                case SpatialShape.Cone:
                    configParams.TryAddInt(EffectParamKeys.QueryRadius, spatial.RadiusCm);
                    configParams.TryAddInt(EffectParamKeys.QueryHalfAngle, spatial.HalfAngleDeg);
                    break;
                case SpatialShape.Rectangle:
                    configParams.TryAddInt(EffectParamKeys.QueryHalfWidth, spatial.HalfWidthCm);
                    configParams.TryAddInt(EffectParamKeys.QueryHalfHeight, spatial.HalfHeightCm);
                    break;
                case SpatialShape.Ring:
                    configParams.TryAddInt(EffectParamKeys.QueryRadius, spatial.RadiusCm);
                    configParams.TryAddInt(EffectParamKeys.QueryInnerRadius, spatial.InnerRadiusCm);
                    break;
                // ... Line 同理
            }
            break;
        // case QueryStrategy.HexAdjacent:
        //     configParams.TryAddInt(EffectParamKeys.QueryHexRange, hexRange);
        //     break;
        // case QueryStrategy.Relationship:
        //     configParams.TryAddInt(EffectParamKeys.QueryRelationType, relType);
        //     break;
    }
}

// ── TargetFilterParams 组件（筛）──
if (typeDef.HasComponent(ComponentFlags.TargetFilterParams))
{
    configParams.TryAddInt(EffectParamKeys.FilterMaxTargets, maxTargets);
    // RelationFilter/ExcludeSource/LayerMask 保持结构字段
}

// ── TargetDispatchParams 组件（做）──
if (typeDef.HasComponent(ComponentFlags.TargetDispatchParams))
{
    if (payloadEffectTemplateId > 0)
        configParams.TryAddInt(EffectParamKeys.DispatchPayloadEffectId, payloadEffectTemplateId);
    // ContextMapping 保持结构字段
}

// ── ForceParams 组件 ──
if (typeDef.HasComponent(ComponentFlags.ForceParams))
{
    configParams.TryAddInt(EffectParamKeys.ForceXAttribute, forceXAttribute);
    configParams.TryAddInt(EffectParamKeys.ForceYAttribute, forceYAttribute);
}
```

**效果**：
- 注入逻辑由类型定义表驱动，而非分散在各处的 if/switch
- Target 子系统三层独立：Query 参数按策略子类型细化，Filter 和 Dispatch 参数是通用的
- 新增 QueryStrategy（如 HexAdjacent）只需在 TargetQueryParams 分支中添加 case，Filter/Dispatch 不受影响
- Circle 的 effect 只注入 `_ep.queryRadius`，不浪费槽位给 halfAngle/halfWidth 等

## 2.3 ConfigParams 扩容

`MAX_PARAMS`: 16 → 32。

容量预算：
- 结构参数注入：~2（通用 duration/period）+ ~3（resolver，按 shape 变化）= ~5
- 用户自定义参数：~20+
- 总计：~25，32 留有余量

struct 大小变化：~160 bytes → ~320 bytes。对于 4096 template 的 registry，总内存从 ~640KB 增加到 ~1.3MB，可接受。

# 3 CallerParams —— 运行时参数覆盖

## 3.1 EffectRequest 扩展

```csharp
public struct EffectRequest
{
    public int RootId;
    public Entity Source;
    public Entity Target;
    public Entity TargetContext;
    public int TemplateId;
    public EffectConfigParams CallerParams;   // 调用方参数覆盖（唯一命名参数通道）
    public bool HasCallerParams;
}
```

## 3.2 EffectCallerParams 组件

```csharp
// 新增文件: src/Core/Gameplay/GAS/Components/EffectCallerParams.cs
public struct EffectCallerParams
{
    public EffectConfigParams Params;
}
```

**生命周期**：
- 创建时：若 `EffectRequest.HasCallerParams`，则在 effect entity 上附加 `EffectCallerParams` 组件。
- Phase 执行时：每次 `SetConfigContext` 前，merge template ConfigParams + entity CallerParams。
- 销毁时：随 entity 一起销毁。

## 3.3 MergeFrom 方法

```csharp
// 新增到 EffectConfigParams
public void MergeFrom(in EffectConfigParams caller)
{
    for (int i = 0; i < caller.Count; i++)
    {
        int key = caller.Keys[i];
        bool found = false;
        for (int j = 0; j < Count; j++)
        {
            if (Keys[j] == key)
            {
                // 覆盖：caller wins
                Types[j] = caller.Types[i];
                IntValues[j] = caller.IntValues[i];
                FloatValues[j] = caller.FloatValues[i];
                found = true;
                break;
            }
        }
        if (!found && Count < MAX_PARAMS)
        {
            // 新增
            Keys[Count] = key;
            Types[Count] = caller.Types[i];
            IntValues[Count] = caller.IntValues[i];
            FloatValues[Count] = caller.FloatValues[i];
            Count++;
        }
    }
}
```

## 3.4 Merge 发生在所有 SetConfigContext 调用处

三个 effect 系统中，所有调用 `SetConfigContext` 的地方统一改为：

```csharp
private EffectConfigParams BuildMergedConfig(in EffectTemplateData tpl, Entity effectEntity)
{
    var merged = tpl.ConfigParams;
    if (World.Has<EffectCallerParams>(effectEntity))
        merged.MergeFrom(World.Get<EffectCallerParams>(effectEntity).Params);
    return merged;
}
```

| 系统 | 改造点 |
|---|---|
| `EffectApplicationSystem` | 创建 entity 时：merge → 读结构覆盖 → 创建 entity；执行 OnResolve/OnHit/OnApply 时：merge → SetConfigContext |
| `EffectLifetimeSystem` | 执行 OnPeriod/OnExpire/OnRemove 时：merge → SetConfigContext |
| `EffectProposalProcessingSystem` | 执行 OnPropose/OnCalculate 时：merge → SetConfigContext |

## 3.5 结构覆盖的读取

`EffectApplicationSystem` 在创建 effect entity 时，从 merged ConfigParams 读取结构参数的最终值：

```csharp
var merged = tpl.ConfigParams;
if (request.HasCallerParams)
    merged.MergeFrom(request.CallerParams);

// 从 merged 读取结构参数（caller 值已覆盖 template 注入的默认值）
int finalDuration = merged.TryGetInt(EffectParamKeys.DurationTicks, out var d) ? d : tpl.DurationTicks;
int finalPeriod = merged.TryGetInt(EffectParamKeys.PeriodTicks, out var p) ? p : tpl.PeriodTicks;

// 创建 effect entity with final values
var entity = GameplayEffectFactory.CreateEffect(world, rootId, source, target,
    finalDuration, lifetimeKind, finalPeriod, ...);

// 附加 CallerParams 组件（仅在有覆盖时）
if (request.HasCallerParams)
    world.Add(entity, new EffectCallerParams { Params = request.CallerParams });
```

## 3.6 JSON 配置 schema

**SSOT**：JSON 格式的完整定义（字段表、示例、校验规则、废除字段）见独立配置结构文档：

- PresetType 定义表 JSON → `docs/04_游戏逻辑/08_能力系统/03_配置结构/01_EffectPresetType定义_配置结构.md`
- EffectTemplate 模板 JSON → `docs/04_游戏逻辑/08_能力系统/03_配置结构/02_EffectTemplate配置_配置结构.md`

本文只描述架构决策，不重复配置 schema。
