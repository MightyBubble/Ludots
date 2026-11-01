# B7: 解除控制(净化)

> 清单编号: B7 | 游戏示例: LoL水银QSS, Dota Lotus Orb, DS翻滚解控

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，移除自身所有控制效果和负面状态。

## Ludots实现

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
AbilityExecSpec:
  Item[0]: EffectSignal → cleanse_effect
    → Phase Graph:
      1. 遍历 target 的 GameplayTagContainer
      2. 移除所有带 "cc" 前缀的 tag
      3. 或: 移除所有 EffectTemplate 带 "debuff" tag 的 active effects
```

需要批量移除匹配特定模式的标签或效果。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| `graph.aim.rule.none` | AimInteractionMod | 无目标 |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| GameplayTagContainer | ✅ 已有 | 标签管理 |
| Phase Graph | ✅ 已有 | 效果处理流程 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| 批量 Tag/Effect 清除 Graph op | P2 | 需要 Graph op 来批量移除匹配某 pattern 的 tag/effect |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
