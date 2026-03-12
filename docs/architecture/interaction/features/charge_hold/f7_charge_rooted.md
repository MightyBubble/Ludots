# F7: 蓄力时完全不能移动

> 清单编号: F7 | 游戏示例: LoL Xerath Q, DS部分法术

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **Direction**（或 None，依技能类型）
- **Acquisition**: **Explicit**

按住蓄力期间完全无法移动（rooted），松开后恢复移动能力。

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillQ"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Direction
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
    - TagClip: "rooted"    // 完全不可移动

  EventGate: wait for "release" event

  OnRelease (from .End order):
    - EffectSignal: launch_projectile (或其他释放效果)
    - RemoveTag("charging")
    - RemoveTag("rooted")
```

**关键点**：
- GrantedTags 包含 `"rooted"` 标签
- Movement system 检查该 tag，完全阻止移动输入
- 与 F6（可缓慢移动）的区别仅在 tag 类型：`rooted` vs `slow_50`

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| HeldPolicy.StartEnd | ✅ 已有 | InputOrderMapping 生成 .Start/.End order 对 |
| EffectClip + GrantedTags | ✅ 已有 | 蓄力效果 + 标签授予 |
| Movement system tag 检查 | ⚠️ **需确认** | Movement system 需检查 `rooted` tag 并阻止移动 |
| Graph ops (Load/WriteSelfAttribute) | ✅ 已有 | 每 tick 累积 charge_amount |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Movement system 支持 rooted tag | P1 | 检查 `rooted` tag，完全阻止移动输入 |

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
- `docs/architecture/interaction/features/charge_hold/f6_charge_slow_move.md` — F6: 蓄力时可缓慢移动
