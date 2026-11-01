# F4: 按住维持效果（盾/格挡）

> 清单编号: F4 | 游戏示例: DS举盾L1, GoW举盾, OW Reinhardt盾

## 交互层

- **InputConfig**: ReactsTo = **DownAndUp**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

按住期间持续维持效果（如格挡、减伤、移动减速），松开即停止。

## Ludots 映射

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


## 实现方案

```
Down → Order: activate_shield
  → EffectClip: shield_effect (duration=MaxDuration)
  → GrantedTags: ["blocking", "slow_movement"]
  → Modifiers: [{ attr: damage_reduction, op: Override, value: 0.8 }]

Up → Order: deactivate_shield
  → Remove shield_effect, remove tags
```

**关键点**：
- EffectClip 持续时长可设为 `Infinite` 或 `MaxDuration`（如耐久值耗尽）
- GrantedTags 可触发 Movement system 减速逻辑（如 `slow_movement` tag → 移动速度 × 0.5）
- Up 事件触发 RemoveEffect 或 ExpireEffect，自动清理 tags 和 modifiers

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| 按下与松开 | 交互状态 | 按下激活 `pref.aim.cast_mode` 指向的手势状态，松开确认走 `graph.aim.release_confirm` |
| EffectClip duration | ✅ 已有 | 持续效果，可设 Infinite |
| GrantedTags | ✅ 已有 | 标签授予/移除 |
| Attribute Modifiers | ✅ 已有 | 属性修改器系统 |
| RemoveEffect on Up | ✅ 已有 | .End order 触发效果移除 |

## 新增需求

无。所有依赖组件已实现；Movement system 需检查 `slow_movement` 或 `rooted` tag 以限制移动速度。

## 相关文档

- `docs/architecture/interaction/features/06_charge_hold_release.md` — Charge/Hold/Release 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单 §F
- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
