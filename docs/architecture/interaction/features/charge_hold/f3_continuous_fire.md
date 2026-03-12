# F3: 持续连射

> 清单编号: F3 | 游戏示例: OW Soldier/Bastion/Tracer

## 交互层

- **InputConfig**: ReactsTo = **Down**（持续，每帧/每周期触发）
- **TargetMode**: **Direction**
- **Acquisition**: **Explicit**

按住期间持续向当前瞄准方向发射，松开即停止。

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillFire"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
  isSkillMapping: true
  heldPolicy: EveryFrame    // 按住期间每帧触发 order
```

## 实现方案

```
AbilityExecSpec:
  PeriodicSearch (period=3 ticks, auto-repeat while held):
    → Direction from continuous cursor tracking
    → LaunchProjectile each period
```

备选方案：

```
// 也可用 StartEnd 启动/停止射击循环
Down → 启动 toggle-like 射击循环:
  EffectClip: fire_loop (duration=Infinite)
    → OnPeriod (period=3 ticks):
      1. ReadBlackboard(aim_direction)
      2. LaunchProjectile(direction=aim_direction)

Up → 停止 fire_loop（RemoveEffect）
```

推荐使用 `heldPolicy: EveryFrame`；AbilityExecSystem 内置周期控制，每次 order 提交均触发一次子弹发射，天然对应连射节奏。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| HeldPolicy.EveryFrame | ✅ 已有 | 按住期间每帧提交 order |
| AbilityExecSpec 周期触发 | ✅ 已有 | PeriodicSearch + LaunchProjectile |
| ProjectileRuntimeSystem | ✅ 已有 | 弹道推进+碰撞 |
| Cursor direction → Blackboard | ✅ 已有（参见 F1 需求） | 实时更新瞄准方向 |

## 新增需求

无。`HeldPolicy.EveryFrame` 已实现；连射节奏由 AbilityExecSpec 的 period 参数控制。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
