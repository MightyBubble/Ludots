# F7: 蓄力时完全不能移动

> 清单编号: F7 | 游戏示例: LoL Xerath Q, DS部分法术

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **Direction**（或 None，依技能类型）
- **Acquisition**: **Explicit**

按住蓄力期间完全无法移动（rooted），松开后恢复移动能力。

## Ludots 映射

目标规则图是 `graph.aim.rule.ground_only`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
| 按下与松开 | 交互状态 | 按下激活 `pref.aim.cast_mode` 指向的手势状态，松开确认走 `graph.aim.release_confirm` |
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
