---
文档类型: 配置结构
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - EffectPresetType 定义表
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/01_技术设计/04_Effect类型体系_架构设计.md
---

# EffectPresetType 定义 配置结构

# 1 概述

PresetType 定义表描述"什么叫一种 Effect 类型"。每个条目声明：

- 组合了哪些参数组件
- 哪些 Phase 活跃
- 每个活跃 Phase 的默认处理器（C# 回调或 Graph 程序）
- LifetimeKind 约束

配置文件路径：`GAS/preset_types.json`。

注册方式：混合。内置类型由引擎加载 JSON 注册；mod 可在 C# 中调用 `PresetTypeRegistry.Register()` 追加。JSON 与 C# 注册的执行路径等价。

# 2 完整 JSON Schema

```json
[
  {
    "id": "InstantDamage",
    "components": ["ModifierParams", "PhaseGraphBindings"],
    "activePhases": ["OnPropose", "OnCalculate", "OnHit", "OnApply"],
    "constraints": {
      "allowedLifetimes": ["Instant"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "DoT",
    "components": [
      "ModifierParams", "DurationParams",
      "PhaseGraphBindings", "PhaseListenerSetup"
    ],
    "activePhases": [
      "OnPropose", "OnCalculate", "OnHit", "OnApply",
      "OnPeriod", "OnExpire"
    ],
    "constraints": {
      "allowedLifetimes": ["After", "Infinite"]
    },
    "defaultPhaseHandlers": {
      "onPeriod": { "main": "Graph.DOT.TickDamage" }
    }
  },
  {
    "id": "Heal",
    "components": ["ModifierParams", "PhaseGraphBindings"],
    "activePhases": ["OnPropose", "OnCalculate", "OnApply"],
    "constraints": {
      "allowedLifetimes": ["Instant"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "HoT",
    "components": [
      "ModifierParams", "DurationParams",
      "PhaseGraphBindings", "PhaseListenerSetup"
    ],
    "activePhases": [
      "OnPropose", "OnCalculate", "OnApply",
      "OnPeriod", "OnExpire"
    ],
    "constraints": {
      "allowedLifetimes": ["After", "Infinite"]
    },
    "defaultPhaseHandlers": {
      "onPeriod": { "main": "Graph.HOT.TickHeal" }
    }
  },
  {
    "id": "Buff",
    "components": [
      "ModifierParams", "DurationParams",
      "PhaseGraphBindings", "PhaseListenerSetup"
    ],
    "activePhases": [
      "OnPropose", "OnCalculate", "OnApply",
      "OnExpire", "OnRemove"
    ],
    "constraints": {
      "allowedLifetimes": ["After", "Infinite", "UntilTagRemoved", "WhileTagPresent"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "SearchAreaDamage",
    "components": [
      "ModifierParams",
      "TargetQueryParams", "TargetFilterParams", "TargetDispatchParams",
      "PhaseGraphBindings"
    ],
    "activePhases": [
      "OnPropose", "OnCalculate", "OnResolve", "OnHit", "OnApply"
    ],
    "constraints": {
      "allowedLifetimes": ["Instant"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "Persistent",
    "components": [
      "DurationParams",
      "TargetQueryParams", "TargetFilterParams", "TargetDispatchParams",
      "PhaseGraphBindings", "PhaseListenerSetup"
    ],
    "activePhases": [
      "OnApply", "OnPeriod", "OnResolve", "OnHit",
      "OnExpire", "OnRemove"
    ],
    "constraints": {
      "allowedLifetimes": ["After", "Infinite"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "ApplyForce2D",
    "components": ["ForceParams"],
    "activePhases": ["OnPropose", "OnApply"],
    "constraints": {
      "allowedLifetimes": ["Instant"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "LaunchProjectile",
    "components": [
      "ProjectileParams",
      "TargetQueryParams", "TargetFilterParams", "TargetDispatchParams",
      "PhaseGraphBindings"
    ],
    "activePhases": [
      "OnPropose", "OnApply", "OnResolve", "OnHit"
    ],
    "constraints": {
      "allowedLifetimes": ["After"]
    },
    "defaultPhaseHandlers": {}
  },
  {
    "id": "CreateUnit",
    "components": ["UnitCreationParams"],
    "activePhases": ["OnPropose", "OnApply"],
    "constraints": {
      "allowedLifetimes": ["Instant"]
    },
    "defaultPhaseHandlers": {}
  }
]
```

# 3 顶层字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | string | **必填** | 类型唯一标识，对应 `EffectPresetType` 枚举值名。大驼峰。 |
| `components` | string[] | **必填** | 该类型组合的参数组件名列表。决定 effect template 需要填哪些配置块、编译时注入哪些 `_ep.*` 键。 |
| `activePhases` | string[] | **必填** | 该类型有意义的 Phase 列表。不在此列表中的 Phase 的 Graph 绑定会被 Loader 忽略并告警。 |
| `constraints` | object | 可选 | 类型约束。 |
| `constraints.allowedLifetimes` | string[] | 可选 | 允许的 `lifetime` 值列表。若 effect template 的 `lifetime` 不在此列表中，Loader fail-fast。 |
| `defaultPhaseHandlers` | object | 可选 | 类型级别的默认 Phase 处理器（Main slot）。effect template 的 `phaseGraphs` 可覆盖。 |

## 3.1 defaultPhaseHandlers 子字段

```json
"defaultPhaseHandlers": {
  "onPeriod": {
    "main": "Graph.DOT.TickDamage"
  },
  "onCalculate": {
    "main": "Graph.DamageFormula.Standard"
  }
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `{phaseName}` | object | Phase 名（camelCase）。 |
| `{phaseName}.main` | string | Main slot 的 Graph 程序名。注册到 `PresetBehaviorRegistry`（整合后为 `PresetTypeDefinition.PhaseHandlers`）。 |

**执行路径**：Main slot 的处理器可以是此处声明的 Graph，也可以是 C# 注册时提供的 delegate。两者走同一条 `EffectPhaseExecutor` 的 Pre → Main → Post 管线。

# 4 参数组件定义

组件是代码定义的积木块。每个组件声明一组字段（schema）和对应的 JSON 键名。

## 4.1 组件清单

组件分为两类：**参数组件**（定义 JSON 数据字段和 `_ep.*` 键）和**能力组件**（声明结构性能力）。

### 参数组件

| 组件名 | JSON 键 | 说明 |
|---|---|---|
| `ModifierParams` | `modifiers` | 属性修改器列表 |
| `DurationParams` | `duration` | 持续时间、周期、时钟 |
| `TargetQueryParams` | `targetQuery` | 目标查询策略 |
| `TargetFilterParams` | `targetFilter` | 目标过滤条件 |
| `TargetDispatchParams` | `targetDispatch` | 目标分发配置 |
| `ForceParams` | `configParams` 中的 `_ep.forceX/Y` | 2D 物理力 |
| `ProjectileParams` | `projectile` | 投射物参数（未来） |
| `UnitCreationParams` | `unitCreation` | 创建单位参数（未来） |

### 能力组件

| 组件名 | JSON 键 | 说明 |
|---|---|---|
| `PhaseGraphBindings` | `phaseGraphs` | 允许 per-template 配置 Pre/Post Graph 绑定，受 `activePhases` 约束 |
| `PhaseListenerSetup` | `phaseListeners` | 允许注册反应式 Phase 监听器。仅限创建 entity 的类型（非纯 Instant） |

## 4.2 ModifierParams

JSON 键：`modifiers`（数组）

```json
"modifiers": [
  { "attribute": "Health", "op": "Add", "value": -20.0 },
  { "attribute": "Armor", "op": "Multiply", "value": 0.8 }
]
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `attribute` | string | **必填** | 属性注册名 |
| `op` | string | 可选 | `Add`（默认）/ `Multiply` / `Override` |
| `value` | float | **必填** | 修改值 |

**容量**：`EffectModifiers.CAPACITY`（当前 8），超出 fail-fast。

## 4.3 DurationParams

JSON 键：`duration`（对象）

```json
"duration": {
  "ticks": 300,
  "periodTicks": 50,
  "clockId": "FixedFrame"
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `ticks` | int | **必填** | 总持续 tick 数。`Infinite` lifetime 时填 0。 |
| `periodTicks` | int | 可选 | 周期 tick 数。0 = 无周期（OnPeriod 不触发）。 |
| `clockId` | string | 可选 | `FixedFrame`（默认）/ `Step` / `Turn` |

**编译注入**：`_ep.durationTicks`, `_ep.periodTicks`, `_ep.clockId`。

## 4.4 TargetQueryParams

JSON 键：`targetQuery`（对象）

```json
"targetQuery": {
  "strategy": "BuiltinSpatial",
  "spatial": {
    "shape": "Cone",
    "radius": 800,
    "halfAngle": 45
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `strategy` | string | **必填** | `BuiltinSpatial` / `GraphProgram` / 未来: `Relationship`, `HexAdjacent` |
| `spatial` | object | 当 `strategy=BuiltinSpatial` | 空间查询参数 |
| `graphProgramId` | string | 当 `strategy=GraphProgram` | Graph 程序名 |

### 4.4.1 spatial 子字段（按 shape 分组）

| shape | 必填字段 | 可选字段 | 编译注入的 `_ep.*` 键 |
|---|---|---|---|
| `Circle` | `radius` | — | `_ep.queryRadius` |
| `Cone` | `radius`, `halfAngle` | — | `_ep.queryRadius`, `_ep.queryHalfAngle` |
| `Rectangle` | `halfWidth`, `halfHeight` | `rotation` | `_ep.queryHalfWidth`, `_ep.queryHalfHeight`, `_ep.queryRotation` |
| `Line` | `length`, `halfWidth` | — | `_ep.queryLength`, `_ep.queryHalfWidth` |
| `Ring` | `radius`, `innerRadius` | — | `_ep.queryRadius`, `_ep.queryInnerRadius` |

**校验规则**：

1. `shape` 不在枚举中 → fail-fast。
2. 缺少必填字段 → fail-fast。
3. 存在该 shape 不需要的字段 → 告警。

## 4.5 TargetFilterParams

JSON 键：`targetFilter`（对象）

```json
"targetFilter": {
  "relationFilter": "Hostile",
  "excludeSource": true,
  "maxTargets": 8,
  "layerMask": ["Unit", "Hero"]
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `relationFilter` | string | 可选 | `All`（默认）/ `Hostile` / `Friendly` / `Neutral` / `NotFriendly` / `NotHostile` |
| `excludeSource` | bool | 可选 | 是否排除施法者。默认 false。 |
| `maxTargets` | int | 可选 | 最大目标数。0 = 无限制（仅受 budget 限制）。 |
| `layerMask` | string[] | 可选 | 目标必须属于的 Layer 名列表。null/空 = 不过滤。 |

**编译注入**：`_ep.filterMaxTargets`。`relationFilter` / `excludeSource` / `layerMask` 为非数值型，保持结构字段，不注入 ConfigParams。

## 4.6 TargetDispatchParams

JSON 键：`targetDispatch`（对象）

```json
"targetDispatch": {
  "payloadEffect": "Effect.Moba.Cone.E.Hit",
  "contextMapping": {
    "payloadSource": "OriginalSource",
    "payloadTarget": "ResolvedEntity",
    "payloadTargetContext": "OriginalTarget"
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `payloadEffect` | string | **必填** | 对每个目标施加的 effect template ID |
| `contextMapping` | object | 可选 | Source/Target/TargetContext 映射。不填则用 AOE 默认映射。 |

**编译注入**：`_ep.dispatchPayloadEffectId`。`contextMapping` 为结构体，保持结构字段。

### 4.6.1 contextMapping 子字段

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `payloadSource` | string | `OriginalSource` | 新 EffectRequest 的 Source |
| `payloadTarget` | string | `ResolvedEntity` | 新 EffectRequest 的 Target |
| `payloadTargetContext` | string | `OriginalTarget` | 新 EffectRequest 的 TargetContext |

可选值：`OriginalSource` / `OriginalTarget` / `OriginalTargetContext` / `ResolvedEntity`。

**预设快捷方式**：

| 模式 | payloadSource | payloadTarget | payloadTargetContext |
|---|---|---|---|
| AOE（默认） | OriginalSource | ResolvedEntity | OriginalTarget |
| Reflect | OriginalTarget | OriginalSource | OriginalTarget |
| Redirect | OriginalSource | OriginalTargetContext | OriginalTarget |

## 4.7 ForceParams

无独立 JSON 键。通过 `configParams` 中的保留键传递：

```json
"configParams": {
  "_ep.forceXAttribute": { "type": "int", "value": 0 },
  "_ep.forceYAttribute": { "type": "int", "value": 0 }
}
```

引擎加载时 `forceXAttribute` / `forceYAttribute` 通过 `AttributeRegistry` 解析为 int ID。

**编译注入**：`_ep.forceXAttribute`, `_ep.forceYAttribute`。

## 4.8 ProjectileParams（未来）

JSON 键：`projectile`（对象）

```json
"projectile": {
  "speed": 2000,
  "range": 1200,
  "arcHeight": 0
}
```

字段待定义。

## 4.9 UnitCreationParams（未来）

JSON 键：`unitCreation`（对象）

```json
"unitCreation": {
  "unitType": "Unit.Skeleton",
  "count": 3,
  "offsetRadius": 200
}
```

字段待定义。

## 4.10 PhaseGraphBindings（能力组件）

JSON 键：`phaseGraphs`（对象）

声明该类型允许 per-template 配置 Pre/Post Graph 绑定。

```json
"phaseGraphs": {
  "onCalculate": {
    "pre": "Graph.DamageFormula.Standard",
    "post": "Graph.DamageBonus.ElementalCheck",
    "skipMain": false
  }
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `{phaseName}.pre` | string | Pre slot Graph 程序名 |
| `{phaseName}.post` | string | Post slot Graph 程序名 |
| `{phaseName}.skipMain` | bool | 跳过 Main slot（PresetType 默认处理器）。默认 false。 |

**约束**：`phaseGraphs` 中的 phase 名必须在该 PresetType 的 `activePhases` 中。

**与 defaultPhaseHandlers 的关系**：`defaultPhaseHandlers` 定义 Main slot 的类型级默认处理器；`phaseGraphs` 定义 Pre/Post slot 的模板级覆盖。`skipMain=true` 可跳过 Main，由 Pre/Post 完全接管。

**不声明此组件时**：该类型的 effect template 不允许配置 `phaseGraphs` 字段，Loader 遇到 → fail-fast。典型场景：纯工具类型（`ApplyForce2D`、`CreateUnit`），其 Phase 逻辑完全由 C# 硬编码，不允许 Graph 介入。

## 4.11 PhaseListenerSetup（能力组件）

JSON 键：`phaseListeners`（数组）

声明该类型允许注册反应式 Phase 监听器。

```json
"phaseListeners": [
  {
    "listenTag": "Effect.Damage",
    "phase": "onApply",
    "scope": "target",
    "action": "graph",
    "graphProgram": "Graph.Shield.Absorb"
  }
]
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `listenTag` | string | 可选 | 监听的 effect tag。空 = 通配。 |
| `listenEffectId` | string | 可选 | 监听的 effect template id。空 = 通配。 |
| `phase` | string | **必填** | 监听的 Phase 名。 |
| `scope` | string | 可选 | `target`（默认）/ `source` |
| `action` | string | 可选 | `graph`（默认）/ `event` / `both` |
| `graphProgram` | string | 条件必填 | action 含 `graph` 时必填。 |
| `eventTag` | string | 条件必填 | action 含 `event` 时必填。 |
| `priority` | int | 可选 | 执行优先级。高 = 先执行。默认 0。 |

**硬约束**：`PhaseListenerSetup` 仅允许出现在非纯 Instant 类型上（Listener 需要 entity 作为载体注册/注销）。若 `constraints.allowedLifetimes` 仅含 `["Instant"]`，则不允许声明此组件，Loader → fail-fast。

**不声明此组件时**：该类型的 effect template 不允许配置 `phaseListeners` 字段，Loader 遇到 → fail-fast。

详细架构见 `docs/04_游戏逻辑/08_能力系统/01_技术设计/07_EffectPhaseListener_架构设计.md`。

# 5 组件与活跃 Phase 的映射关系

组件的存在隐含了某些 Phase 的活跃性。这是 PresetType 定义表的核心逻辑。

| 组件 | 隐含的活跃 Phase |
|---|---|
| ModifierParams | OnPropose, OnCalculate, OnApply |
| DurationParams | OnPeriod（若 periodTicks > 0）, OnExpire |
| TargetQueryParams | OnResolve |
| TargetFilterParams | OnHit |
| TargetDispatchParams | —（依赖 Query + Filter 的结果） |
| ForceParams | OnApply |
| PhaseGraphBindings | —（不隐含 Phase，但受 activePhases 约束） |
| PhaseListenerSetup | —（不隐含 Phase，监听的是其他 effect 的 Phase） |
| — | OnRemove（所有 duration effect 通用） |

**PresetType 的 `activePhases` 是显式声明**，不是自动推导。上表仅为设计指导。Loader 用 `activePhases` 做校验，不做隐式推导。

# 6 校验规则

| 规则 | 触发条件 | 行为 |
|---|---|---|
| id 唯一 | 重复 id | fail-fast |
| components 合法 | 组件名不在已注册列表中 | fail-fast |
| activePhases 合法 | Phase 名不在 8 Phase 枚举中 | fail-fast |
| allowedLifetimes 合法 | Lifetime 名不在枚举中 | fail-fast |
| defaultPhaseHandlers phase 合法 | handler 的 phase 不在 `activePhases` 中 | 告警 |
| defaultPhaseHandlers graph 存在 | Graph 程序名未注册 | 告警 |
| PhaseListenerSetup + 纯 Instant | `allowedLifetimes` 仅含 `Instant` 却声明了 `PhaseListenerSetup` | fail-fast |

# 7 C# 注册等价接口

```csharp
// 引擎启动时加载 preset_types.json，等价于以下 C# 调用：
PresetTypeRegistry.Register(new PresetTypeDefinition
{
    Type = EffectPresetType.DoT,
    Components = ComponentFlags.ModifierParams | ComponentFlags.DurationParams
               | ComponentFlags.PhaseGraphBindings | ComponentFlags.PhaseListenerSetup,
    ActivePhases = PhaseFlags.OnPropose | PhaseFlags.OnCalculate | PhaseFlags.OnHit
                 | PhaseFlags.OnApply | PhaseFlags.OnPeriod | PhaseFlags.OnExpire,
    Constraints = new PresetTypeConstraints
    {
        AllowedLifetimes = LifetimeFlags.After | LifetimeFlags.Infinite,
    },
    PhaseHandlers =
    {
        [EffectPhaseId.OnPeriod] = new PhaseHandler { MainGraphId = GraphIdRegistry.GetId("Graph.DOT.TickDamage") },
    },
});
```

Mod 作者可以在 C# 中调用相同 API 注册自定义类型。新组件也需要 C# 定义。
