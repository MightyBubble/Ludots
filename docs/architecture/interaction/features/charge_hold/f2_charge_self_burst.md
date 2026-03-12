# F2: 蓄力+自身爆发

> 清单编号: F2 | 游戏示例: DS R2蓄力重击, GoW蓄力重斧

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

按住蓄力，松开时以自身为中心释放 AoE；蓄力时长决定爆发半径与伤害缩放。

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
  heldPolicy: StartEnd
```

## 实现方案

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
    - Search preset (self AoE):
      → radius = base_radius * charge_amount
    - FanOutApplyEffect(charged_slam_damage):
      → damage = base_damage * charge_amount
```

与 F1 的区别仅在 OnRelease：F1 发射弹道，F2 以自身为圆心向外爆发。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| HeldPolicy.StartEnd | ✅ 已有 | InputOrderMapping 生成 .Start/.End order 对 |
| AbilityExecSpec EventGate | ✅ 已有 | 等待 release 事件后执行 OnRelease |
| Graph ops (Load/WriteSelfAttribute) | ✅ 已有 | 每 tick 累积 charge_amount |
| EffectClip duration/period | ✅ 已有 | 限制最大蓄力时长 |
| Search preset (self AoE) | ✅ 已有 | 以自身为中心的圆形搜索 |
| FanOutApplyEffect | ✅ 已有 | 批量施加效果到命中目标 |

## 新增需求

无。所有依赖组件已实现；radius 参数通过 ConfigParams override 传入搜索 preset 即可。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
