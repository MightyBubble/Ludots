# Feature: Charge / Hold / Release Abilities (F1–F9)

> 清单覆盖: F1 蓄力射击, F2 蓄力爆发, F3 连射, F4 持续维持, F5 瞄准+施放, F6 可移动蓄力, F7 不可移动蓄力, F8 满蓄自动释放, F9 持续属性变化

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: Direction / None (依子类)
- **Acquisition**: Explicit

## 核心洞察

HoldRelease 在交互层只是 "Down 事件 + Up 事件"。蓄力积累是 **Effect tick 写 Attribute**, 不在交互层。

```
Down → 触发 "begin_charge" Order (SubmitMode: Immediate)
       → AbilityExecSystem 启动 charge_effect
       → charge_effect 每 tick: charge_amount += dt

Up   → 触发 "release_charge" Order
       → AbilityExecSystem 读取 charge_amount → 执行释放
```

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame    // Down
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
  heldPolicy: StartEnd         // 启用 Start/End 分离

// heldPolicy=StartEnd 时:
// PressedThisFrame → submit order with suffix ".Start"
// ReleasedThisFrame → submit order with suffix ".End"
```

**已有**: `HeldPolicy.StartEnd` 在 `InputOrderMapping`, `InputOrderMappingSystem` 自动生成 Start/End 对。

## 实现方案

### F1: 蓄力+方向射击

```
AbilityDefinition:
  OnActivate (from .Start order):
    - EffectClip: charge_accumulator (duration=MaxChargeTicks)
      → OnPeriod Graph:
        1. LoadSelfAttribute(charge_amount)
        2. AddFloat(dt_normalized)
        3. ClampFloat(0, 1.0)
        4. WriteSelfAttribute(charge_amount)
    - TagClip: "charging"

  EventGate: wait for "release" event

  OnRelease (from .End order → fires release event):
    - EffectSignal: launch_projectile
      → ConfigParams override: damage = base_damage * charge_amount
      → ProjectileConfig: speed = base_speed * charge_amount
```

### F2: 蓄力+自身爆发

```
同 F1, 但 OnRelease:
  - Search preset (self AoE, radius = base_radius * charge_amount)
  - FanOutApplyEffect(charged_slam_damage)
```

### F3: 持续连射

```
InputOrderMapping:
  heldPolicy: EveryFrame  // 按住期间每帧触发

AbilityExecSpec:
  PeriodicSearch (period=3 ticks, auto-repeat while held):
    → Direction from continuous cursor tracking
    → LaunchProjectile each period
```

- 或: Down → 启动 toggle-like 射击循环, Up → 停止

### F4: 按住维持(盾/格挡)

```
Down → Order: activate_shield
  → EffectClip: shield_effect (duration=MaxDuration)
  → GrantedTags: ["blocking", "slow_movement"]
  → Modifiers: [{ attr: damage_reduction, op: Override, value: 0.8 }]

Up → Order: deactivate_shield
  → Remove shield_effect, remove tags
```

### F5: 按住瞄准+另一键施放

```
L2 (Aim) mapping:
  heldPolicy: StartEnd
  selectionType: None
  orderTypeKey: "aim_mode"

R1/R2 (Fire) mapping:
  trigger: PressedThisFrame
  selectionType: Direction
  orderTypeKey: "castAbility"
  // 仅在 HasTag("aiming") 时有效 → precondition 检查
```

- Aim mode: Down → AddTag("aiming") + 切换 camera, Up → RemoveTag("aiming")
- Fire: 检查 "aiming" tag → 施放投射物

### F6/F7: 可移动/不可移动蓄力

```
区别仅在 GrantedTags:
  F6: GrantedTags: ["charging", "slow_50"]  // 减速但可移动
  F7: GrantedTags: ["charging", "rooted"]   // 完全不可移动

Movement system 检查 rooted/slow tags 限制移动
```

### F8: 满蓄自动释放

```
charge_accumulator Effect 的 OnExpire Phase:
  → 自动触发 release effect (无需 Up 事件)
  → 等效于系统自动发出 .End order
```

- **需要**: charge effect 到期时自动 fire "release" event → 触发 EventGate

### F9: 持续按住属性变化

```
heldPolicy: StartEnd
Down → EffectClip: shift_effect
  → OnPeriod Graph:
    1. LoadSelfAttribute(agility)
    2. AddFloat(+1 per tick)
    3. WriteSelfAttribute(agility)
    4. LoadSelfAttribute(strength)
    5. AddFloat(-1 per tick)
    6. WriteSelfAttribute(strength)

Up → 停止 shift_effect
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| HeldPolicy.StartEnd | ✅ 已有 |
| HeldPolicy.EveryFrame | ✅ 已有 |
| AbilityExecSpec EventGate | ✅ 已有 |
| Graph ops (Load/WriteSelfAttribute) | ✅ 已有 |
| EffectClip duration/period | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| Charge effect 到期自动 fire event (满蓄自动释放) | P1 | F8 |
| Cursor direction 持续写入 Blackboard (蓄力中更新瞄准) | P1 | F1 |
