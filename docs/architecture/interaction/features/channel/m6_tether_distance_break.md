# Mechanism: M6 — Tether Distance Break (牵引/连接: 距离过远断裂)

> **Examples**: Dota Io Tether, Razor Link, LoL Karma W, LoL Morgana R

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

**建立牵引连接**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → create_tether
    → EffectClip: tether_effect
      period: 5 ticks
      duration: 300 ticks
      target: order_target

  OnPeriod Phase Graph:
    1. distance = CalcDistance(caster, target)
    2. if distance > break_range (800cm):
       → DestroyEffect(self)  // 断裂
    3. else:
       → ApplyEffect(tether_tick_effect, target)  // 持续效果
```

**牵引效果** (可选):
```
tether_tick_effect:
  → ModifyAttribute(target, move_speed, multiply=0.7)  // 减速
  → ApplyDamage(target, 10)  // 持续伤害
```

**视觉表现**:
```
Presenter: TetherLine(origin=caster, target=target)
  color: purple
  lifetime: 与 EffectClip 同步
  break_visual: 距离过远时显示断裂特效
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| EffectClip (持续效果) | ✅ 已有 |
| CalcDistance Graph op | ✅ 已有 |
| Conditional break logic | ✅ 已有 |
| DestroyEffect | ✅ 已有 |
| TetherLine Presenter | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| TetherLine Presenter | P2 | 连接线渲染 (表现层) |
| Distance-based break | P1 | 距离检测并销毁效果 |
