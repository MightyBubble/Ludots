# J5: 单手/双手持握切换

> 清单编号: J5 | 游戏示例: DS Y键切换双持

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键在单手/双手持握间切换，连击组和属性随之变化；机制与 J4 相同，weapon tag 改为 grip tag。

## Ludots实现

```
同 J4，但 form tag 是 grip_mode:
  AddTag("grip:one_hand")  → one-hand combo set
  AddTag("grip:two_hand")  → two-hand combo set

Down → Execute:
  if HasTag("grip:one_hand"):
    RemoveTag("grip:one_hand") + AddTag("grip:two_hand")
  else:
    RemoveTag("grip:two_hand") + AddTag("grip:one_hand")

AbilityExecSystem 激活时:
  读取 grip tag → 路由到对应连击 ability 组
```

Grip 切换复用 J2/J4 的 form-based ability mapping，grip tag 作为 form tag 使用。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | grip tag 切换 |
| AbilityStateBuffer | ⚠️ 需要扩展 | form-based ability mapping，与 J2/J3/J4 共用需求 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Form-based ability mapping (slot + tag → ability) | P1 | 与 J2/J3/J4 共用，grip tag 作为 form tag |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
