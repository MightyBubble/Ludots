# Mechanism: M3 — Mobile Channel (引导中可缓慢移动)

> **Examples**: 部分边走边引导的技能, LoL Karthus Q spam, Dota Witch Doctor Death Ward

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None** (或 Direction)
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

**引导技能 (允许移动)**:
```
AbilityExecSpec:
  Item[0]: TagClip "channeling", duration=180 ticks
  Item[1]: ModifyAttribute(self, move_speed, multiply=0.5)  // 减速50%
  Item[2-N]: EffectSignal @ periodic ticks (每 30 ticks)
    → ApplyEffect(channel_damage_tick)
```

**关键差异** (与 M1 对比):
- **不添加** `rooted` tag
- 移动系统正常运行
- 可选: 添加 `move_speed` 减速 modifier

**打断机制**:
```
同 M1: CC 移除 "channeling" tag → InterruptAny
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| TagClip | ✅ 已有 |
| channeling tag | ✅ 已有 |
| ModifyAttribute (move_speed) | ✅ 已有 |
| PeriodicEffect | ✅ 已有 |
| InterruptAny on tag loss | ✅ 已有 |

## 新增需求

无 — 全部依赖已满足。只需不添加 `rooted` tag 即可允许移动。
