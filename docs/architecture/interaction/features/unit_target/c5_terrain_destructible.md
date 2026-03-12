# C5: 地形/可破坏物

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillD"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Entity
  isSkillMapping: true
  argsTemplate: { i0: 4 }  // ability slot
```

### InteractionMode 变体:

| Mode | 行为 |
|------|------|
| TargetFirst | 先选单位, 按键即施放 |
| SmartCast | 按键时取光标下单位, 立即施放 |
| AimCast | 按键进入选取模式, 点击确认 |
| SmartCastWithIndicator | 按住显示范围, 松开施放 |

所有模式已在 `InputOrderMappingSystem` 中实现。

## 实现方案

```
需求: SelectionRule 支持 EntityLayer filter
实现:
  1. 树木/石头等设为特定 EntityLayer (Destructible)
  2. SelectionRule: filter=Destructible layer
  3. Phase Graph 处理: 对可破坏物施加效果 (HP减为0等)
```

- 已有: `QueryFilterLayer` Graph op (op 115)

## 依赖组件

| 组件 | 状态 |
|------|------|
| InputOrderMapping.Entity | ✅ 已有 |
| InteractionModeType (4种) | ✅ 已有 |
| QueryFilterLayer Graph op | ✅ 已有 (op 115) |
| EntityLayer 系统 | ✅ 已有 |
| SelectionRuleRegistry | ✅ 已有 |

## 新增需求

无 — 所有依赖组件已实现。可通过 SelectionRule 配置 EntityLayer filter 实现。
