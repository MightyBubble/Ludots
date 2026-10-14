# Mechanism: K1 — Ground Trap / Mine (地面放陷阱/地雷)

> **Examples**: LoL Teemo R, Caitlyn夹子, Jhin E

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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

> ⚠️ Architecture note: Graph VM cannot perform structural changes (creating/deleting entities, mounting components). Entity destruction must go through an OnApply handler → RuntimeEntitySpawnQueue, not directly in response chain execution.

```
ResponseChainListener:
  eventTagId: on_enemy_enter_radius
  precondition: distance < 100cm
  responseType: Chain
    → ApplyEffect(explosion_damage)
    → QueueDestroy(self)  // via OnApply handler → RuntimeEntitySpawnQueue
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
