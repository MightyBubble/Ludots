# Mechanism: D6 — Teleport to Point (传送到指定地面点)

> **Examples**: Dota NP Teleport, LoL TF R落点

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Point**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Position
  isSkillMapping: true
  argsTemplate: { i0: 1 }
```

OrderSubmitter 将点击位置写入 `SpatialBlackboardKey`。

## 实现方案

```
AbilityExecSpec:
  Item[0]: TagClip "channeling" @ tick 0, duration=180 ticks (3s channel)
  Item[1]: EffectSignal @ tick 180 → teleport_effect
    → Phase Graph: 设置 caster position = order_position
```

- Performer: GroundOverlay(Circle) 在目标点显示传送指示器
- 可选: 添加 "interruptible" tag, 受击打断引导

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialBlackboardKey | ✅ 已有 |
| TagClip | ✅ 已有 |
| EffectSignal | ✅ 已有 |
| Position 修改 Graph op | ✅ 已有 |

## 新增需求

无 — 所有依赖已满足。
