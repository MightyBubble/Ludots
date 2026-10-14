# Mechanism: K3 — Controllable Summon (召唤可控制单位)

> **Examples**: Dota Lone Druid熊, SC2产兵, LoL Annie Tibbers

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
AbilityExecSpec:
  Item[0]: EffectSignal → create_summon
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "bear_summon"
      position: order_point
      ownerId: caster
      lifetime: permanent (或 duration=3600 ticks)
      controllable: true
      tags: ["summon", "controllable", "selectable"]
      attributes:
        health: 1000
        move_speed: 300
```

**控制机制**:
- 召唤物可被玩家选中 (selectable tag)
- 接受移动/攻击命令 (与主角共享输入系统)
- 死亡后: 技能进入冷却, 可重新召唤

## 依赖组件

| 组件 | 状态 |
|------|------|
| CreateUnit handler | ✅ 已有 |
| UnitCreationDescriptor | ✅ 已有 |
| Controllable flag | ✅ 已有 |
| Selection system | ✅ 已有 |
| Unit command system | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Summon ownership link | P1 | 召唤物与召唤者关联 |
| Max summon count | P2 | 限制同时存在数量 |
| Summon death callback | P2 | 死亡时触发冷却 |
