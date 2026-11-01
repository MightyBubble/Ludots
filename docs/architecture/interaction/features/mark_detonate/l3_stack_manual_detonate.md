# Mechanism: L3 — Stack Manual Detonate (叠层+手动引爆可选)

> **Examples**: LoL Tristana E(叠加或等时间到), LoL Vel'Koz Q叠层

## 交互层

交互层与 base ability 相同。叠层+引爆均通过 Attribute + Tag + ResponseChain 实现。

- **Skill A (叠层)**: Down + Unit, Explicit
- **Skill B (引爆)**: Down + Unit, Explicit (同一技能或另一键)

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

**Skill A — 附加标记并叠层**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → apply_or_stack_mark
    → Phase Graph:
      if target HasTag("tristana_e"):
        → ModifyAttributeAdd(target, e_stacks, +1)
        → CheckPrecondition: e_stacks >= max_stacks
        → If true: trigger_detonate()
      else:
        → AddTag("tristana_e", target, duration=permanent_until_removed)
        → SetAttribute(target, e_stacks, 1)
```

**手动引爆 (Skill B 或右键命令)**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → manual_detonate
    → Phase Graph (target=e_marked_enemy):
      1. damage = base + (e_stacks * per_stack_bonus)
      2. ApplyEffect(explosion_damage, value=damage)
      3. RemoveTag("tristana_e")
      4. SetAttribute(target, e_stacks, 0)
```

**叠层伤害公式**:
```
total_damage = base_damage + (e_stacks * stack_damage_bonus)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| AddTag / HasTag | ✅ 已有 |
| ModifyAttributeAdd | ✅ 已有 |
| Conditional Phase Graph | ✅ 已有 |
| ReadAttribute in damage formula | ✅ 已有 |
| RemoveTag | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Conditional stack-or-create logic | P1 | Phase Graph 分支 |
| Damage scaling by stack count | P1 | 属性驱动伤害公式 |
