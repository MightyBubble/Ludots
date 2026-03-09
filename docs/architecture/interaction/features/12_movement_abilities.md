# Feature: Movement Abilities (I1–I12)

> 清单覆盖: I1 方向冲刺, I2 目标冲刺, I3 瞬移, I4 勾爪, I5 摆荡, I6 击退, I7 拉拽, I8 位置互换, I9 墙壁攀爬, I10 滑翔, I11 冲锋, I12 击飞

## 交互层

| 子类 | TargetMode | 说明 |
|------|-----------|------|
| I1 方向冲刺 | Direction | 朝光标方向 |
| I2 目标冲刺 | Unit | 冲向目标 |
| I3 瞬移 | Point | 瞬移到点 |
| I4 勾爪 | Point / Context | 选锚点 |
| I5 摆荡 | None (DownAndUp) | 按住摆荡 |
| I6/I7 击退/拉拽 | Unit | 目标受力 |
| I8 互换 | Unit | 交换位置 |
| I9/I10 攀爬/滑翔 | None | 被动/按住 |
| I11 冲锋 | Direction | 持续前进 |
| I12 击飞 | Unit / Direction | 弹起目标 |

## 实现方案

### I1/I2/I3: 冲刺/瞬移

```
核心: DisplacementPreset (已有)

I1 方向冲刺:
  DisplacementDescriptor:
    directionMode: Fixed (from order direction)
    distanceCm: 300
    durationTicks: 10

I2 目标冲刺:
  DisplacementDescriptor:
    directionMode: ToTarget
    target: order_target entity

I3 瞬移:
  Phase Graph: 直接设置 position = order_point (无 displacement, 即时)
```

- 已有: `DisplacementRuntimeSystem`, `DisplacementDirectionMode`

### I4: 勾爪/蛛丝

```
ContextScored (Spider-Man/Sekiro):
  候选锚点: 环境中有 "grapple_point" tag 的实体
  Precondition: grapple_point in view cone + distance < range
  Score: angle_score + distance_score
  执行: Displacement(ToTarget, target=grapple_point)
```

### I5: 摆荡 (Spider-Man)

```
DownAndUp:
  Down → 搜索头顶锚点 → 创建 tether effect → 物理摆荡
  WhileHeld (effect tick): 应用摆荡物理 (pendulum force)
  Up → 断开 tether, 保留惯性速度

需要: 物理层支持 pendulum/swing 约束 (超出 GAS 范围, 属于 Physics2D 层)
```

### I6: 击退 + I7: 拉拽

```
I6 击退:
  EffectPresetType: Displacement
  DisplacementDescriptor:
    directionMode: AwayFromSource
    distanceCm: 400

I7 拉拽:
  DisplacementDescriptor:
    directionMode: TowardSource
    distanceCm: 500
```

- 已有: `DisplacementDirectionMode.AwayFromSource`, `TowardSource`

### I8: 位置互换

```
Phase Graph:
  1. 保存 caster.position → temp_pos
  2. 设 caster.position = target.position
  3. 设 target.position = temp_pos
```

### I11: 冲锋 (Reinhardt)

```
Down → Direction:
  1. AddTag("charging")
  2. EffectClip: displacement_continuous (direction=fixed, speed=high)
  3. PeriodicSearch (period=1 tick, front cone):
     if hit enemy → stun + damage + stop charge
  4. Duration: 180 ticks (max charge time)
  5. OnExpire: RemoveTag("charging")
```

### I12: 击飞

```
EffectPresetType: Displacement
DisplacementDescriptor:
  directionMode: Fixed (upward)
  distanceCm: 200 (height)
  AddTag on target: "airborne" (duration=effect duration)
```

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| 即时位置设置 (teleport, 非 displacement) | P1 | I3, I8 |
| 摆荡物理 (Physics2D 层) | P3 | I5 |
| Displacement 碰撞检测 (冲锋撞人停止) | P2 | I11 |
| Airborne tag + 空中物理 | P2 | I12 |
