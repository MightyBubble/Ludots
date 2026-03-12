# J4: 武器切换 → 连击组变

> 清单编号: J4 | 游戏示例: DS换武器, GoW斧/拳切换

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键切换当前装备武器，连击组随武器变化；机制与 J2 相同，form tag 改为 weapon tag。

## Ludots实现

```
同 J2，但 form tag 是 weapon_id:
  AddTag("weapon:axe")   → axe combo set
  AddTag("weapon:fists") → fists combo set

Down → Execute:
  RemoveTag 当前 weapon tag
  AddTag 下一个 weapon tag

AbilityExecSystem 激活时:
  读取 weapon tag → 路由到对应连击 ability 组
```

武器切换复用 J2 的 form-based ability mapping，weapon tag 作为 form tag 使用，slot 路由逻辑完全相同。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | weapon tag 切换 |
| AbilityStateBuffer | ⚠️ 需要扩展 | form-based ability mapping，与 J2/J3 共用需求 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Form-based ability mapping (slot + tag → ability) | P1 | 与 J2/J3 共用，weapon tag 作为 form tag |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
