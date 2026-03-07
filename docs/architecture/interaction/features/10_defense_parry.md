# Feature: Defense / Parry / Counter (H1–H9)

> 清单覆盖: H1 持续格挡, H2 精准弹反, H3 反击提示, H4 闪避, H5 精准闪避奖励, H6 弹反后处决, H7 反射弹道, H8 定向闪入, H9 吸收转化

## 交互层

| 子类 | InputConfig | TargetMode | Acquisition |
|------|-----------|-----------|------------|
| H1 持续格挡 | DownAndUp | None | Explicit |
| H2 弹反 | Down | None | Explicit |
| H3 反击 | Down | None | ContextScored |
| H4/H5 闪避 | Down | Direction | Explicit |
| H7 反射 | Down | None | Explicit |
| H8 闪入 | Down | Direction | ContextScored |

## 实现方案

### H1: 持续格挡

```
HeldPolicy: StartEnd
Down → AddTag("blocking") + EffectClip(damage_reduction modifier)
Up → RemoveTag("blocking") + 移除 effect
```

### H2: 精准弹反

```
Down → AddTag("parry_window", duration=6 ticks)  // 很短的时间窗口
受击时 ResponseChainListener:
  eventTagId: incoming_attack
  precondition Graph: caster HasTag("parry_window")
  responseType: Hook (取消伤害) + Chain (创建 posture_damage effect on attacker)
  RemoveTag("parry_window")
  AddTag("parry_success", duration=30 ticks)  // 用于 H6 弹反后处决
```

**关键**: 弹反 = 极短时间窗口内的 `ResponseChainListener` Hook

### H3: 反击提示 (Arkham)

```
Acquisition: ContextScored
ContextGroup "counter":
  candidate: counter_attack
    precondition: 有敌人 HasTag("attacking") 且距离 < 300cm
    score: distance + timing

InputOrderMapping:
  actionId: "Counter"
  trigger: PressedThisFrame
  contextGroupId: "counter"

执行:
  自动选取 HasTag("attacking") 的最近敌人 → 播放反击动画 → damage
```

### H4/H5: 闪避 + 精准闪避

```
Down → EffectSignal:
  1. 向 input_direction 位移 (Displacement preset, distance=200cm)
  2. AddTag("iframe", duration=10 ticks) — 无敌帧
  3. AddTag("dodge_window", duration=3 ticks) — 精准判定窗口 (H5)

H5 扩展:
  受击时 ResponseChainListener:
    precondition: HasTag("dodge_window")
    responseType: Hook (取消伤害) + Chain (AddTag("perfect_dodge_buff", duration=120 ticks))
```

### H6: 弹反后处决窗口

```
Sekiro 忍杀:
  posture Attribute 累积 (每次被弹反 +N)
  when posture >= max → AddTag("posture_broken")

  deathblow ability:
    precondition: target HasTag("posture_broken") + distance < 100cm
    → 处决动画 + instant kill
```

### H7: 反射弹道

```
Genji Deflect:
Down → AddTag("deflecting", duration=120 ticks)
  ResponseChainListener:
    eventTagId: projectile_hit
    precondition: HasTag("deflecting")
    responseType: Hook (取消命中) + Chain:
      → LaunchProjectile(direction=反射方向, source=self)
```

### H8: 闪入特定攻击 (Mikiri Counter)

```
ContextScored:
  candidate: mikiri_counter
    precondition: nearest enemy HasTag("thrusting") + distance < 200cm
    score: timing_window * angle_score
    执行: Displacement 向 target 方向 + damage + posture_damage
```

### H9: 吸收转化

```
ResponseChainListener:
  eventTagId: incoming_damage
  responseType: Modify (reduce damage to 0) + Chain:
    → effect: ModifyAttributeAdd(energy, absorbed_amount)
```

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| AbilityActivationRequireTags | P0 | H6 (处决需 posture_broken) |
| ContextGroup | P0 | H3, H8 |
| Projectile Hook + Redirect | P2 | H7 |
