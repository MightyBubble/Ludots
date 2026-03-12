# F8: 满蓄自动释放

> 清单编号: F8 | 游戏示例: LoL Varus Q满蓄自动射, OW Widow满充

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**（但满蓄时无需 Up 事件）
- **TargetMode**: **Direction**（或 None，依技能类型）
- **Acquisition**: **Explicit**

按住蓄力，达到最大蓄力时长后自动释放，无需松开按键。

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
      → OnExpire Phase:
        → 自动触发 "release" event (无需 Up 事件)
    - TagClip: "charging"

  EventGate: wait for "release" event

  OnRelease (from .End order OR auto-fire from OnExpire):
    - EffectSignal: launch_projectile
      → ConfigParams override: damage = base_damage * charge_amount
```

**关键点**：
- EffectClip 的 `OnExpire` Phase 自动 fire "release" event
- EventGate 监听该 event，触发 OnRelease 执行
- 玩家可提前松开（触发 .End order），或等待满蓄自动释放（触发 OnExpire）

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| HeldPolicy.StartEnd | ✅ 已有 | InputOrderMapping 生成 .Start/.End order 对 |
| EffectClip OnExpire Phase | ⚠️ **需确认** | 到期时自动 fire event |
| AbilityExecSpec EventGate | ✅ 已有 | 等待 release 事件后执行 OnRelease |
| Graph ops (Load/WriteSelfAttribute) | ✅ 已有 | 每 tick 累积 charge_amount |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Charge effect 到期自动 fire event | P1 | EffectClip OnExpire Phase 需支持自动触发 "release" event |

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
