# Mechanism: K1 — Ground Trap / Mine (地面放陷阱/地雷)

> **Examples**: LoL Teemo R, Caitlyn夹子, Jhin E

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
```

## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → create_trap
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "trap_mine"
      position: order_point
      ownerId: caster
      lifetime: 600 ticks (或 permanent until triggered)
      tags: ["trap", "invisible_to_enemy"]
```

**触发逻辑** (在 trap entity 上):
```
ResponseChainListener:
  eventTagId: on_enemy_enter_radius
  precondition: distance < 100cm
  responseType: Chain
    → ApplyEffect(explosion_damage)
    → DestroyEntity(self)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| ResponseChainListener | ✅ 已有 |
| Proximity trigger | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Proximity detection | P1 | 敌人进入半径触发 |
| Invisible tag support | P2 | 隐身机制 |
| Lifetime expiration | P1 | 超时自动销毁 |
