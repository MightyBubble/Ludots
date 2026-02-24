---
文档类型: 配置结构
创建日期: 2026-02-08
最后更新: 2026-02-08
维护人: X28技术团队
文档版本: v1.0
适用范围: 游戏逻辑 - 能力系统(GAS) - EffectTemplate 模板配置
状态: 审阅中
依赖文档:
  - docs/04_游戏逻辑/08_能力系统/03_配置结构/01_EffectPresetType定义_配置结构.md
---

# EffectTemplate 配置 配置结构

# 1 概述

每个 effect template 是一个 JSON 对象，必须声明 `presetType`。Loader 根据 PresetType 定义表校验必填组件、注入 `_ep.*` 保留键到 ConfigParams。

配置文件路径：`GAS/effects.json`。支持多文件合并（ConfigPipeline）和 Mod 覆盖。

# 2 顶层字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | string | **必填** | 全局唯一模板 ID。推荐 `Effect.{域}.{名}` 命名。 |
| `presetType` | string | **必填** | PresetType 定义表中的已注册 id。 |
| `tags` | string[] | 可选 | 第一个 tag 注册为 TagId，用于 PhaseListener 匹配。 |
| `lifetime` | string | **必填** | `Instant` / `After` / `Infinite` / `UntilTagRemoved` / `WhileTagPresent` |
| `participatesInResponse` | bool | 可选 | 是否参与 ResponseChain。默认 true。 |
| `duration` | object | 条件必填 | DurationParams 组件。详见 PresetType 配置文档 4.3 节。 |
| `modifiers` | array | 条件必填 | ModifierParams 组件。详见 PresetType 配置文档 4.2 节。 |
| `targetQuery` | object | 条件必填 | TargetQueryParams 组件。详见 PresetType 配置文档 4.4 节。 |
| `targetFilter` | object | 条件必填 | TargetFilterParams 组件。详见 PresetType 配置文档 4.5 节。 |
| `targetDispatch` | object | 条件必填 | TargetDispatchParams 组件。详见 PresetType 配置文档 4.6 节。 |
| `phaseGraphs` | object | 条件允许 | Per-Phase 的 Pre/Post Graph 绑定。仅当 PresetType 声明了 `PhaseGraphBindings` 组件时允许。 |
| `configParams` | object | 可选 | 用户自定义 Graph 参数 + `_ep.*` 保留键显式覆盖。 |
| `phaseListeners` | array | 条件允许 | 反应式 Phase 监听器。仅当 PresetType 声明了 `PhaseListenerSetup` 组件时允许。 |

**条件必填**：由 PresetType 定义表的 `components` 决定。若该类型声明了 `DurationParams` 组件，则 `duration` 字段必填。缺少 → fail-fast。

# 3 按类型的完整示例

## 3.1 InstantDamage

```json
{
  "id": "Effect.Moba.Damage.Q",
  "presetType": "InstantDamage",
  "tags": ["Effect.Damage"],
  "lifetime": "Instant",
  "modifiers": [
    { "attribute": "Health", "op": "Add", "value": -20.0 }
  ]
}
```

## 3.2 DoT

```json
{
  "id": "Effect.Burn.DOT",
  "presetType": "DoT",
  "tags": ["Effect.Damage.Fire"],
  "lifetime": "After",
  "duration": {
    "ticks": 300,
    "periodTicks": 50
  },
  "modifiers": [
    { "attribute": "Health", "op": "Add", "value": -5.0 }
  ],
  "configParams": {
    "tickDamage": { "type": "float", "value": 5.0 }
  },
  "phaseGraphs": {
    "onCalculate": { "pre": "Graph.DamageFormula.Standard" }
  }
}
```

## 3.3 Buff

```json
{
  "id": "Effect.Buff.SpeedBoost",
  "presetType": "Buff",
  "tags": ["Effect.Buff.Movement"],
  "lifetime": "After",
  "duration": {
    "ticks": 600
  },
  "modifiers": [
    { "attribute": "MoveSpeed", "op": "Multiply", "value": 1.3 }
  ]
}
```

## 3.4 SearchAreaDamage（带 Target 三层拆分）

```json
{
  "id": "Effect.Moba.AOE.Fireball",
  "presetType": "SearchAreaDamage",
  "tags": ["Effect.Damage.Fire"],
  "lifetime": "Instant",
  "targetQuery": {
    "strategy": "BuiltinSpatial",
    "spatial": {
      "shape": "Circle",
      "radius": 500
    }
  },
  "targetFilter": {
    "relationFilter": "Hostile",
    "excludeSource": true,
    "maxTargets": 8
  },
  "targetDispatch": {
    "payloadEffect": "Effect.Fireball.Hit"
  }
}
```

## 3.5 SearchAreaDamage — Cone

```json
{
  "id": "Effect.Moba.Damage.E",
  "presetType": "SearchAreaDamage",
  "tags": ["Effect.Damage"],
  "lifetime": "Instant",
  "targetQuery": {
    "strategy": "BuiltinSpatial",
    "spatial": {
      "shape": "Cone",
      "radius": 800,
      "halfAngle": 45
    }
  },
  "targetFilter": {
    "relationFilter": "Hostile",
    "excludeSource": true,
    "maxTargets": 8
  },
  "targetDispatch": {
    "payloadEffect": "Effect.Moba.Cone.E.Hit",
    "contextMapping": {
      "payloadSource": "OriginalSource",
      "payloadTarget": "ResolvedEntity",
      "payloadTargetContext": "OriginalTarget"
    }
  }
}
```

## 3.6 Persistent（持续区域效果）

```json
{
  "id": "Effect.Blizzard.Zone",
  "presetType": "Persistent",
  "tags": ["Effect.Zone.Ice"],
  "lifetime": "After",
  "duration": {
    "ticks": 600,
    "periodTicks": 60
  },
  "targetQuery": {
    "strategy": "BuiltinSpatial",
    "spatial": {
      "shape": "Circle",
      "radius": 400
    }
  },
  "targetFilter": {
    "relationFilter": "Hostile",
    "excludeSource": true
  },
  "targetDispatch": {
    "payloadEffect": "Effect.Blizzard.Tick"
  }
}
```

## 3.7 ApplyForce2D

```json
{
  "id": "Effect.Preset.ApplyForce2D",
  "presetType": "ApplyForce2D",
  "tags": ["Effect.ApplyForce"],
  "lifetime": "Instant",
  "configParams": {
    "_ep.forceXAttribute": { "type": "int", "value": 0 },
    "_ep.forceYAttribute": { "type": "int", "value": 0 }
  }
}
```

## 3.8 带 PhaseListener 的护盾 Buff

```json
{
  "id": "Effect.Shield.Basic",
  "presetType": "Buff",
  "tags": ["Effect.Buff.Shield"],
  "lifetime": "After",
  "duration": {
    "ticks": 600
  },
  "configParams": {
    "shieldAmount": { "type": "float", "value": 100.0 }
  },
  "phaseListeners": [
    {
      "listenTag": "Effect.Damage",
      "phase": "onApply",
      "scope": "target",
      "action": "graph",
      "graphProgram": "Graph.Shield.Absorb"
    }
  ]
}
```

# 4 phaseGraphs 字段

Per-Phase 的 Pre/Post Graph 绑定。Key 为 phase 名（camelCase）。

```json
"phaseGraphs": {
  "onCalculate": {
    "pre": "Graph.DamageFormula.Standard",
    "post": "Graph.DamageBonus.ElementalCheck",
    "skipMain": false
  },
  "onApply": {
    "post": "Graph.Fireball.OnApply"
  }
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `pre` | string | Pre slot Graph 程序名 |
| `post` | string | Post slot Graph 程序名 |
| `skipMain` | bool | 是否跳过 Main slot（PresetType 的默认处理器或 C# 逻辑）。默认 false。 |

**校验**：phase 名必须在该 PresetType 的 `activePhases` 中，否则告警。

# 5 configParams 字段

用户自定义 Graph 参数。也可用于显式设置 `_ep.*` 保留键覆盖编译注入的默认值。

```json
"configParams": {
  "tickDamage": { "type": "float", "value": 5.0 },
  "fireElement": { "type": "int", "value": 1 },
  "chainEffect": { "type": "effectTemplate", "value": "Effect.Chain.Lightning" },
  "_ep.durationTicks": { "type": "int", "value": 999 }
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `type` | string | `float` / `int` / `effectTemplate` |
| `value` | any | 值。`effectTemplate` 类型为 template ID 字符串。 |

**容量**：`MAX_PARAMS = 32`。编译注入 + 用户自定义合计不超过 32。

# 6 phaseListeners 字段

反应式监听器：当**其他** effect 到达指定 Phase 时，执行 Graph 或发射事件。

```json
"phaseListeners": [
  {
    "listenTag": "Effect.Damage",
    "listenEffectId": "",
    "phase": "onApply",
    "scope": "target",
    "action": "graph",
    "graphProgram": "Graph.Shield.Absorb",
    "eventTag": "",
    "priority": 0
  }
]
```

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `listenTag` | string | 可选 | 监听的 effect tag。空 = 通配。 |
| `listenEffectId` | string | 可选 | 监听的 effect template id。空 = 通配。 |
| `phase` | string | **必填** | 监听的 Phase 名。 |
| `scope` | string | 可选 | `target`（默认）/ `source`。在谁身上监听。 |
| `action` | string | 可选 | `graph`（默认）/ `event` / `both`。 |
| `graphProgram` | string | 条件必填 | action 含 `graph` 时必填。 |
| `eventTag` | string | 条件必填 | action 含 `event` 时必填。 |
| `priority` | int | 可选 | 执行优先级。高 = 先执行。默认 0。 |

# 7 废除字段

以下字段在新 schema 中不再支持，Loader 遇到时 fail-fast 报错：

| 字段 | 替代 |
|---|---|
| `durationFrames` | `duration.ticks` |
| `periodFrames` | `duration.periodTicks` |
| `durationType` | `lifetime` |
| `teamFilter` | `targetFilter.relationFilter` |
| `excludeFromChain` | `participatesInResponse`（语义反转） |
| `onApplyEffect` / `onPeriodEffect` / `onExpireEffect` / `onRemoveEffect` | `phaseGraphs` |
| `forceXAttribute` / `forceYAttribute` | `configParams._ep.forceXAttribute` / `_ep.forceYAttribute` |
| `targetResolver`（扁平 object） | `targetQuery` + `targetFilter` + `targetDispatch` |

# 8 Loader 校验规则

| 规则 | 行为 |
|---|---|
| `id` 缺失或重复 | fail-fast |
| `presetType` 不在 PresetTypeRegistry 中 | fail-fast |
| `lifetime` 不满足 PresetType 的 `constraints.allowedLifetimes` | fail-fast |
| PresetType 声明的组件对应 JSON 字段缺失 | fail-fast |
| PresetType 未声明的参数组件对应 JSON 字段存在 | 告警（允许，但 Loader 不处理） |
| `phaseGraphs` 存在但 PresetType 未声明 `PhaseGraphBindings` | fail-fast |
| `phaseListeners` 存在但 PresetType 未声明 `PhaseListenerSetup` | fail-fast |
| `phaseGraphs` 中的 phase 不在 `activePhases` 中 | 告警 |
| 废除字段存在 | fail-fast |
| ConfigParams 总数超过 `MAX_PARAMS` | fail-fast |
| `modifiers` 数量超过 `CAPACITY` | fail-fast |
| Graph 程序名未注册 | 告警 |
| `targetDispatch.payloadEffect` 未注册 | 告警 |
