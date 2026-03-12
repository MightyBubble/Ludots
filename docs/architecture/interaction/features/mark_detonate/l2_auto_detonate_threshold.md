# Mechanism: L2 — Auto Detonate Threshold (叠层满自动引爆)

> **Examples**: LoL Vayne W三环, Brand叠满被动, LoL Electrocute

## 交互层

交互层与 base ability 相同。叠层计数与自动触发全部通过 Attribute + ResponseChain 实现。

- **Skill**: Down + Unit, Explicit (正常攻击/技能命中)

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillW"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
```

## 实现方案

**命中时叠层**:
```
AbilityExecSpec (命中 effect):
  OnHit Phase Graph:
    → ModifyAttributeAdd(target, mark_stacks, +1)
```

**自动引爆监听器** (在目标 entity 上, 由初次叠层时附加):
```
ResponseChainListener:
  eventTagId: attribute_changed(mark_stacks)
  precondition Graph:
    → ReadAttribute(self, mark_stacks) >= 3
  responseType: Chain
    → ApplyEffect(proc_damage)       // 真实伤害/爆炸效果
    → SetAttribute(self, mark_stacks, 0)  // 重置叠层
```

**叠层时间窗口** (可选):
```
ModifyAttributeAdd 附带 decay_timer:
  → 一定时间无新叠层: 衰减归零
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| ModifyAttributeAdd | ✅ 已有 |
| ResponseChainListener | ✅ 已有 |
| attribute_changed event | ✅ 已有 |
| Precondition Graph | ✅ 已有 |
| SetAttribute | ✅ 已有 |
| Attribute decay | ⚠️ 需扩展 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Attribute decay timer | P2 | 一段时间不叠层则衰减 |
| Stack visual indicator | P2 | 叠层数显示 (表现层) |
