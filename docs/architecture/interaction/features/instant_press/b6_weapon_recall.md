# B6: 召回投出的武器/物体

> 清单编号: B6 | 游戏示例: GoW 斧头召回(回途造成伤害)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，召回之前投出的武器/物体，回途可造成伤害。

## Ludots实现

目标规则图是 `graph.aim.rule.unit_or_ground`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
实现方案:
  1. 投出斧头时 → CreateUnit(axe_entity, position=target_point)

  2. 召回时:
     → EffectSignal → recall_axe_effect
     → Phase Graph:
       a. 找到 axe_entity (HasTag: thrown_axe, owner=caster)
       b. 设置 axe 的 DisplacementState 朝向 caster
       c. axe 的 PeriodicSearch 沿途造成伤害
```

使用 DisplacementRuntimeSystem 控制武器移动，PeriodicSearch 处理沿途伤害。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| `graph.aim.rule.none` | AimInteractionMod | 无目标 |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| DisplacementRuntimeSystem | ✅ 已有 | 控制武器移动 |
| CreateUnit handler | ✅ 已有 | 创建武器实体 |
| PeriodicSearch | ✅ 已有 | 沿途伤害检测 |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施（Displacement preset）
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
