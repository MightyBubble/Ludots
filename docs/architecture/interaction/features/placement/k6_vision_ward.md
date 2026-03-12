# Mechanism: K6 — Vision Ward (放置视野/侦查装置)

> **Examples**: Dota Observer Ward, SC2 Scan, LoL Control Ward

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "Item"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → place_ward
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "observer_ward"
      position: order_point
      ownerId: caster
      lifetime: 36000 ticks (7分钟 @60fps)
      tags: ["ward", "invisible_to_enemy", "structure"]
      attributes:
        vision_radius: 1600cm
```

**视野机制**:
```
PeriodicEffect (period=5 ticks):
  Phase Graph:
    1. QueryRadius(self, vision_radius, filter=Any)
    2. ForEach entity in result:
       → MarkVisible(entity, to_team=owner_team)
```

**反隐身检测** (如果有):
```
同一 PeriodicEffect 中:
  QueryRadius(self, 300cm, filter=Invisible)
  → MarkVisible(invisible_units)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| Vision system | ⚠️ 需扩展 |
| Invisible tag | ⚠️ 需扩展 |
| Lifetime expiration | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Vision radius system | P1 | 视野计算与雾战 |
| Fog of war | P1 | 敌方隐藏视野区域 |
| Ward inventory limit | P2 | 地图上限制ward数量 |
| Enemy detection | P2 | 检测enemy ward并高亮 |
