# B5: 时间回溯

> 清单编号: B5 | 游戏示例: OW Tracer Recall, LoL Ekko R

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键即生效，回到几秒前的位置（通常伴随无敌帧）。

## Ludots实现

```
InputOrderMapping:
  actionId: "Recall"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true

实现方案:
  1. 挂载一个 Permanent 的 PeriodicEffect (period=1 tick)
     → 每 tick 记录当前位置到 ring buffer (Blackboard 或专用组件)

  2. 激活 Recall 技能时:
     → EffectSignal → recall_effect
     → Phase Graph: 读取 N ticks 前的位置 → 设置当前位置
     → GrantedTags: ["invulnerable", duration=0.5s]
```

需要位置历史记录机制，可使用 Blackboard 或专用组件实现 ring buffer。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| AbilityExecSpec | ✅ 已有 | EffectSignal item |
| PeriodicEffect | ✅ 已有 | 用于记录位置历史 |
| GameplayTagContainer | ✅ 已有 | 授予无敌标签 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| PositionHistory 组件 | P2 | 需要 ring buffer 记录最近 N 帧位置，或使用 Blackboard 存储（但 slot 有限） |

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/02_instant_press.md` — Instant Press 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
