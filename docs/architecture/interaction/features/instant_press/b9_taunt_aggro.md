# B9: 嘲讽/强制聚怪

> 清单编号: B9 | 游戏示例: LoL Shen嘲讽(自身周围), Dota Axe Berserker's Call

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，强制周围敌人攻击自己。

## Ludots实现

目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → taunt_effect
    → Phase Graph:
      1. SpatialQuery(origin=self, radius=400cm, filter=Enemy)
      2. FanOutApplyEffect(taunt_tag_template)
    → EffectTemplate:
      - GrantedTags: ["taunted"]
      - Duration: 150 ticks
      - 配合 AI 系统: 带 "taunted" tag 的单位强制攻击施法者
```

使用 Search preset 对周围敌人施加嘲讽标签，AI 系统根据标签调整行为。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| `graph.aim.rule.none` | AimInteractionMod | 无目标 |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| Search preset | ✅ 已有 | SpatialQuery + FanOut |
| GameplayTagContainer | ✅ 已有 | 授予嘲讽标签 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| AI 系统嘲讽响应 | P2 | AI 系统需要识别 "taunted" 标签并强制攻击施法者 |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
