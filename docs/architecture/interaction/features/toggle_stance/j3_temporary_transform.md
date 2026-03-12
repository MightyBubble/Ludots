# J3: 临时变身 (大招期间)

> 清单编号: J3 | 游戏示例: OW Genji龙刃, GoW Spartan Rage

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按大招键，临时进入特殊形态，持续固定时长后自动恢复原形态。

## Ludots实现

```
Down → Execute:
  AddTag("ultimate_form", duration=360 ticks)
  替换 ability set (同 J2 form tag 路由)
  OnExpire: 恢复原 ability set
```

变身期间通过 form tag 替换整个技能组（与 J2 共用同一机制）。Tag 携带 duration，到期自动移除，`OnExpire` 回调恢复原始技能映射。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | form tag 添加（带 duration） |
| Tag duration / expiry | ✅ 已有 | tag 到期自动移除 |
| AbilityStateBuffer | ⚠️ 需要扩展 | form-based ability mapping，与 J2 共用需求 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Form-based ability mapping (slot + tag → ability) | P1 | 与 J2/J4 共用，AbilityStateBuffer 扩展 |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
