# F2: 蓄力+自身爆发

> 清单编号: F2 | 游戏示例: DS R2蓄力重击, GoW蓄力重斧

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

按住蓄力，松开时以自身为中心释放 AoE；蓄力时长决定爆发半径与伤害缩放。

## Ludots 映射

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| 按下与松开 | 交互状态 | 按下激活 `pref.aim.cast_mode` 指向的手势状态，松开确认走 `graph.aim.release_confirm` |
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
