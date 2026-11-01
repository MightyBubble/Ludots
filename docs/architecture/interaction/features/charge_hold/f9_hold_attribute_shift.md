# F9: 持续按住属性变化

> 清单编号: F9 | 游戏示例: Dota Morphling敏捷/力量Shift

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

按住期间持续改变自身属性（如敏捷 ↑ 力量 ↓），松开即停止变化。

## Ludots 映射

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
AbilityDefinition:
  OnActivate (from .Start order):
    - EffectClip: shift_effect (duration=Infinite)
      → OnPeriod Graph (period=1 tick):
        1. LoadSelfAttribute(agility)
        2. AddFloat(+1 per tick)
        3. WriteSelfAttribute(agility)
        4. LoadSelfAttribute(strength)
        5. AddFloat(-1 per tick)
        6. WriteSelfAttribute(strength)
    - TagClip: "shifting"

  OnDeactivate (from .End order):
    - RemoveEffect(shift_effect)
    - RemoveTag("shifting")
```

**关键点**：
- EffectClip 持续时长设为 `Infinite`，由 Up 事件手动停止
- OnPeriod Graph 每 tick 执行一次属性转移
- 可设置属性上下限（如 agility 不超过 100，strength 不低于 10）

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| 按下与松开 | 交互状态 | 按下激活 `pref.aim.cast_mode` 指向的手势状态，松开确认走 `graph.aim.release_confirm` |
| EffectClip duration=Infinite | ✅ 已有 | 持续效果，直到手动移除 |
| Graph ops (Load/WriteSelfAttribute) | ✅ 已有 | 每 tick 读写属性 |
| RemoveEffect on Up | ✅ 已有 | .End order 触发效果移除 |

## 新增需求

无。所有依赖组件已实现；属性上下限可通过 Graph ops 的 `ClampFloat` 实现。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
