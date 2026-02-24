---
文档类型: 架构设计
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - Effect 类型体系
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/03_配置结构/01_EffectPresetType定义_配置结构.md
---

# Effect 类型体系 架构设计

# 1 设计理念

Effect 的完整定义由三层构成：

1. **技术类型**（LifetimeKind）：决定 effect 的生存方式（瞬时 / 持续 / 无限），是代码定义的硬边界。mod 扩展也需要写 C#。
2. **预设类型**（PresetType）：一张**类型定义表**，每个条目声明"这种类型组合了哪些参数组件、哪些 Phase 活跃、每个 Phase 的默认处理器是什么"。C# 注册，mod 可扩展。参数组件可交叉复用——例如 InstantDamage 和 DoT 都组合了 DamageParams，它们的 OnCalculate 可以共享同一个 damage formula graph。
3. **用户配置**（Phase Graph Bindings）：在类型定义表的基础上，每个 effect template 可以进一步配置 Pre/Post Graph 绑定，覆盖或扩展默认行为。

先有类型，再有组件，再有参数。不存在"裸"的 effect——每个 effect template 必须声明自己的 PresetType，系统据此知道它需要什么参数、走什么 Phase。

# 2 技术类型（EffectLifetimeKind）

代码定义，决定 effect entity 的创建策略和参数有效性。

| LifetimeKind | 语义 | 实现状态 | 是否创建 entity | Duration 有意义 | Period 有意义 |
|---|---|---|---|---|---|
| `Instant` | 一次性，立即结算 | **已实现** | 否（纯 Instant 优化路径：不创建 entity，内联 apply modifiers，但 OnApply Phase Listeners 仍触发） | 否 | 否 |
| `After` | 持续一段时间后过期 | **已实现** | 是 | 是 | 是（若 > 0） |
| `Infinite` | 不自动过期，需显式移除 | **已实现** | 是 | 否（设为 0） | 是（若 > 0） |
| `UntilTagRemoved` | 与 tag 绑定，tag 移除时过期 | **仅枚举定义，未实现** | 是（预期） | 否（预期） | 是（预期） |
| `WhileTagPresent` | tag 存在时持续，tag 消失时过期 | **仅枚举定义，未实现** | 是（预期） | 否（预期） | 是（预期） |

> **注意**：`UntilTagRemoved` 和 `WhileTagPresent` 在 `EffectLifetimeKind` 枚举中有定义（值 3 和 4），但 `EffectLifetimeSystem` 中没有任何对应的过期检测逻辑，`EffectTemplateLoader` 也不会将 JSON 中的 `durationType` 解析为这两个值。它们是**预留的未实现扩展点**。

**扩展规则**：新增 LifetimeKind 必须在 C# 代码中定义枚举值，并在 `EffectLifetimeSystem` 中实现过期检测逻辑。mod 如需新增，必须写 C# 代码。

# 3 预设类型（EffectPresetType）—— 类型定义表

## 3.1 核心概念

PresetType 不只是一个 C# 枚举，而是一张**类型定义表**。每个 PresetType 条目定义：

1. **组合了哪些参数组件**（如 DoT = DamageParams + DurationParams）
2. **哪些 Phase 是活跃的**（如 DoT 有 OnPeriod，InstantDamage 没有）
3. **每个活跃 Phase 的默认处理逻辑**（C# 回调或 Graph 程序，执行路径等价）
4. **约束条件**（如 InstantDamage 必须 Instant，DoT 必须 Durable）

**JSON 配置 schema** → `docs/04_游戏逻辑/08_能力系统/03_配置结构/01_EffectPresetType定义_配置结构.md`

## 3.2 参数组件 —— 代码定义的积木块

参数组件是有固定字段 schema 的、代码定义的积木块。不同 PresetType 通过**组合**这些积木块来定义自己需要什么参数。组件之间可以交叉复用——例如 InstantDamage 和 DoT 都用 DamageParams。

| 参数组件 | 字段集（示意） | 被谁使用 |
|---|---|---|
| ModifierParams | Attribute, Op, Value | 几乎所有伤害/治疗/buff 类型 |
| DurationParams | DurationTicks, PeriodTicks | DoT, HoT, Persistent, Buff |
| DamageParams | BaseDamage, Element, ArmorPen | InstantDamage, DoT, AOE |
| TargetQueryParams | QueryStrategy + 策略参数（见下方展开） | AOE, SearchArea, 连锁 |
| TargetFilterParams | RelationFilter, ExcludeSource, LayerMask, MaxTargets | 带目标查询的类型 |
| TargetDispatchParams | PayloadEffectId, ContextMapping | 带目标查询的类型 |
| ForceParams | ForceXAttribute, ForceYAttribute | ApplyForce2D |
| ProjectileParams | Speed, Range, ArcHeight | LaunchProjectile（未来） |
| UnitCreationParams | UnitType, Count, Offset | CreateUnit（未来） |

**注意**：组件的字段 schema 和解析逻辑是**代码定义**的，用户不需要配置组件的内部结构——用户只选择"我这种类型用哪些组件"。

### 3.2.1 Target 子系统的三层拆分：Query → Filter → Dispatch

当前 `TargetResolverDescriptor` 将"查"和"做"耦合在一个 struct 中。正确的分层是：

```text
  ┌─────────────────────────────────────────────────────────┐
  │ TargetQueryParams（查 —— 填 target list）               │
  │                                                         │
  │ 策略（QueryStrategy）：                                 │
  │   BuiltinSpatial  ─── SpatialQueryParams                │
  │   │                    Shape, Radius, HalfAngle,        │
  │   │                    HalfWidth, HalfHeight, Length...  │
  │   │                                                     │
  │   GraphProgram    ─── GraphProgramId                    │
  │   │                                                     │
  │   Relationship    ─── RelationType（未来）               │
  │   │                    为每个有特定关系的实体触发        │
  │   │                                                     │
  │   HexAdjacent     ─── HexRange, IncludeSelf（未来）     │
  │                       为 hex 网格相邻格子触发           │
  ├─────────────────────────────────────────────────────────┤
  │ TargetFilterParams（筛 —— 验证每个 candidate）          │
  │                                                         │
  │   RelationFilter (Ally/Enemy/Neutral/All)               │
  │   ExcludeSource, LayerMask, MaxTargets                  │
  │   Ring InnerRadius（几何筛选，属于 Spatial 子策略）      │
  ├─────────────────────────────────────────────────────────┤
  │ TargetDispatchParams（做 —— 对每个 target 执行）        │
  │                                                         │
  │   PayloadEffectTemplateId                               │
  │   ContextMapping (Source/Target/TargetContext 映射)      │
  └─────────────────────────────────────────────────────────┘
```

**关键设计决策**：

1. **Query 策略可独立扩展**：新增 `Relationship`、`HexAdjacent` 等查询策略不影响 Filter 和 Dispatch 层。
2. **Filter 和 Dispatch 是通用的**：无论目标是怎么查出来的（空间、关系、hex），过滤和分发逻辑完全相同。
3. **现有代码基础**：`TargetResolverFanOutHelper` 已将实现拆为 `ResolveTargets()`（Query）→ `ValidateAndCollect()`（Filter）→ `PublishFanOutCommands()`（Dispatch），数据模型将跟进。
4. **ConfigParams 映射**：三层各自的参数都注入为 `_ep.*` 键，CallerParams 可覆盖任何一层的参数。

## 3.3 类型定义与活跃 Phase 的关系

每种 PresetType 声明的组件集决定了哪些 Phase 有意义。Phase 是所有 effect 都走的管线，但**组件组合让回调点不同**：

```text
                      共享的 OnCalculate
                      （damage formula graph 可以相同）
                             │
  InstantDamage              │              DoT
  ┌──────────────┐           │           ┌──────────────┐
  │ DamageParams │───────────┤───────────│ DamageParams │
  │ ModifierPrms │           │           │ ModifierPrms │
  └──────────────┘           │           │ DurationPrms │
                             │           └──────────────┘
  活跃 Phase:                │           活跃 Phase:
  OnPropose                  │           OnPropose
  OnCalculate  ◄─────共享────┘           OnCalculate
  OnHit                                  OnHit
  OnApply                                OnApply
                                         OnPeriod  ← DurationParams 带来的
                                         OnExpire  ← DurationParams 带来的
```

## 3.4 类型注册方式 —— 混合模式

基础类型在 C# 中注册（因为可能有 C# 回调逻辑），但组件集和活跃 Phase 是声明式的。**执行路径等价**——无论类型的 Phase 处理是 C# 函数还是 Graph 程序，都走同一条 Pre/Main/Post 管线。

```csharp
// 概念示意（具体实现待定）
PresetTypeRegistry.Register(new PresetTypeDefinition
{
    Type = EffectPresetType.DoT,
    Components = ComponentFlags.DamageParams | ComponentFlags.ModifierParams | ComponentFlags.DurationParams,
    ActivePhases = PhaseFlags.OnPropose | PhaseFlags.OnCalculate | PhaseFlags.OnHit
                 | PhaseFlags.OnApply | PhaseFlags.OnPeriod | PhaseFlags.OnExpire,
    // Main 处理器：可以是 C# delegate，也可以是 Graph ID，执行路径等价
    PhaseHandlers = {
        [OnCalculate] = GraphOrCallback("Graph.DamageFormula.Standard"),
        [OnPeriod]    = GraphOrCallback("Graph.DOT.TickDamage"),
    },
    Constraints = { LifetimeKind = After | Infinite },  // DoT 不能是 Instant
});
```

**扩展路径**：mod 作者在 C# 中调用 `PresetTypeRegistry.Register()` 注册新类型，从现有参数组件中选择组合。如需新的参数组件，也需要写 C# 定义。

## 3.5 当前实现现状与差距

当前代码中 `EffectPresetType` 仅是一个 byte 枚举（None=0, ApplyForce2D=1），没有类型定义表、没有组件声明、没有活跃 Phase 声明。所有"类型决定了什么"的逻辑分散在：

- `EffectTemplateLoader.Compile()` 中的 if/switch（验证约束）
- `ApplyPresetModifiers()` 中的 switch（C# Main 逻辑）
- `PresetBehaviorRegistry` 中的注册（Graph Main 逻辑，生产代码未注册）

**目标**：将这些分散逻辑收拢为一张声明式的类型定义表，使"一种 PresetType 有什么组件、什么 Phase、什么处理器"可在一处看清。

# 4 参数组件与 ConfigParams 的统一

类型定义表声明的参数组件，在运行时统一映射为 ConfigParams 中的 key-value。每个参数组件对应一组 `EffectParamKeys`（详见 `05_Effect参数架构_架构设计.md`）。

这意味着：
- **编辑器**可以根据 PresetType 的组件声明，只显示相关的参数面板。
- **CallerParams** 可以覆盖任何组件的任何参数，因为它们在 ConfigParams 中是等价的。
- **Graph 程序**通过 `LoadConfigFloat/Int` 读取，不区分参数来自哪个组件。

# 5 与 SC2 的对标

SC2 编辑器有 ~15 种 Effect 类型（Damage、Heal、CreateUnit、SearchArea、LaunchMissile、Persistent 等），每种类型有固定的属性面板。

Ludots 的方案本质与 SC2 类似——PresetType 定义表等价于 SC2 的类型枚举，参数组件等价于 SC2 每种类型的属性面板。差异在于：

| 方面 | SC2 | Ludots |
|---|---|---|
| 类型定义 | 引擎内置，不可扩展 | C# 注册表，mod 可扩展 |
| 参数组件 | 类型独占，不可交叉 | 积木式组合，可交叉复用 |
| Phase 处理 | 引擎内置逻辑 | C# 回调或 Graph，执行路径等价 |
| 参数覆盖 | 有限（升级系统） | CallerParams 通用覆盖 |
