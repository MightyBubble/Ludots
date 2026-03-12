# J7: 选择强化哪个技能再施放

> 清单编号: J7 | 游戏示例: LoL Karma R(下个技能增强)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按增强键，下一个技能自动使用增强版本；增强状态由 tag 标记，技能执行时检查 tag 选择 CallerParams slot。

## Ludots实现

```
Down → AddTag("empowered_next", duration=180 ticks)

下一个技能施放时:
  precondition: HasTag("empowered_next")
  → 使用增强版 CallerParams (slot 1 而非 slot 0)
  → RemoveTag("empowered_next")
```

`AbilityExecCallerParamsPool` 已有 4 slots，slot 0 为普通版本，slot 1 为增强版本。技能执行时检查 `empowered_next` tag，存在则使用 slot 1 的 CallerParams，执行后移除 tag。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | empowered_next tag 添加/移除 |
| AbilityExecCallerParamsPool | ✅ 已有 | 4 slots，支持多版本 CallerParams |
| Tag duration / expiry | ✅ 已有 | tag 到期自动移除 |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
