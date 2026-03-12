# Mechanism: M2 — Aim Adjustable Channel (引导中可调整方向)

> **Examples**: LoL Lucian R(微调), Jhin R(锥形内选方向), OW Reaper Death Blossom

## 交互层

- **InputConfig**: ReactsTo = **Down** (初次激活) + **Down** (每发射击)
- **TargetMode**: **Direction** (持续更新)
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping (激活):
  actionId: "SkillR"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

InputOrderMapping (射击):
  actionId: "SkillR_Fire"
  trigger: PressedThisFrame
  orderTypeKey: "channelFire"
  selectionType: Direction
  isSkillMapping: true
```

## 实现方案

**引导激活**:
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" + "aiming_cone", duration=180 ticks
  Item[1]: InputGate → 等待第 1 发 (玩家按 fire)
  Item[2]: EffectSignal → shot_1
    → Phase Graph:
      1. ReadBlackboard(cursor_direction)
      2. CreateProjectile(direction=cursor_direction)
  Item[3]: InputGate → 等待第 2 发
  Item[4]: EffectSignal → shot_2
  ...repeat...
```

**持续方向更新**:
```
每帧 (InputSystem):
  → ReadCursorPosition()
  → CalculateDirection(caster_position, cursor_position)
  → WriteBlackboard(cursor_direction, direction_vector)
```

**视觉指示器**:
```
Performer: ConeOverlay(angle=30°, range=1200cm, color=red)
  rotation: 每帧读取 cursor_direction
  lifetime: 与 channeling tag 同步
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| TagClip | ✅ 已有 |
| InputGate | ✅ 已有 |
| Cursor direction write | ⚠️ 需扩展 |
| ReadBlackboard(direction) | ✅ 已有 |
| CreateProjectile | ✅ 已有 |
| ConeOverlay Performer | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Cursor direction 持续写入 Blackboard | P1 | 每帧更新方向 |
| ConeOverlay Performer | P2 | 锥形瞄准指示器 (表现层) |
| Multi-shot InputGate | P1 | 多次等待玩家输入 |
