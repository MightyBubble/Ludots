# Mechanism: L4 — Position Recall (放置标记→回溯/传送到标记)

> **Examples**: OW Tracer Recall, LoL Zed R回影, LoL Ekko R

## 交互层

- **Phase 1 (标记/存储)**: 自动持续执行 (Periodic effect)
- **Phase 2 (召回激活)**: Down + None, Explicit

## Ludots 映射

```
Phase 1 (自动记录):
  永久挂载的 PeriodicEffect
  period: 1 tick
  → write position to ring buffer attribute

Phase 2 (召回):
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: None
  isSkillMapping: true
```

## 实现方案

**Phase 1 — 持续位置记录**:
```
Permanent PeriodicEffect (period=1 tick):
  OnPeriod Phase Graph:
    → ReadPosition(self)
    → WriteRingBuffer(position_history, current_pos, buffer_size=180)
```

**Phase 2 — 回溯激活**:
```
AbilityExecSpec:
  Item[0]: EffectSignal → recall_to_past
    → Phase Graph:
      1. ReadRingBuffer(position_history, index=180)  // 3秒前 @60fps
      2. Teleport(self, recalled_position)
      3. RestoreAttributes(health=value_at_3s_ago)  // (Tracer机制)
      4. AddVisualEffect("blink_flash")
```

**Zed影子变体** (L4 子变体):
```
Phase 1: CreateUnit(shadow_clone, caster_position)
Phase 2: Swap(caster, shadow) → Teleport(caster, shadow_position)
         → Destroy(shadow)
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| PeriodicEffect permanent | ✅ 已有 |
| Ring buffer attribute | ❌ 需新增 |
| WriteRingBuffer Graph op | ❌ 需新增 |
| ReadRingBuffer Graph op | ❌ 需新增 |
| Teleport handler | ✅ 已有 |
| Attribute snapshot | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ring buffer attribute type | P1 | 存储历史位置序列 |
| Position history snapshot | P1 | 每 tick 写入固定大小缓冲 |
| Attribute restore at T-N | P2 | Tracer 血量回溯机制 |
