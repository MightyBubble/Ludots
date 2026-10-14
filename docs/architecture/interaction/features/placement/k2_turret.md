# Mechanism: K2 — Turret / Building (地面放建筑/炮塔)

> **Examples**: LoL Heimerdinger Q, SC2造建筑, OW Torbjörn炮塔

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → create_turret
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "turret_building"
      position: order_point
      ownerId: caster
      lifetime: permanent (或 duration=1800 ticks)
      tags: ["structure", "attackable"]
      attributes:
        health: 500
        attack_range: 600cm
        attack_damage: 50
```

**炮塔AI逻辑** (在 turret entity 上):
```
PeriodicEffect (period=30 ticks):
  Phase Graph:
    1. QueryRadius(self, attack_range, filter=Enemy)
    2. SelectClosest()
    3. ApplyEffect(auto_attack_projectile, target=selected)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| PeriodicEffect | ✅ 已有 |
| Auto-attack AI | ✅ 已有 |
| Structure tag | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Max turret count limit | P2 | 限制同时存在数量 |
| Placement validation | P2 | 检查地形合法性 |
