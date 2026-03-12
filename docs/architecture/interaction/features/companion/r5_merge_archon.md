# R5: Merge / Archon

## 机制描述
选择两个特定单位合并成一个更强大的新单位（如星际 2 中的两个高阶圣堂武士合并成执政官）。

## 交互层设计
- **Input**: Down
- **Selection**: SelectionGate → 选 2 个特定类型单位
- **Resolution**: Explicit

## 实现要点
```
InputOrderMapping:
  actionId: "MergeUnits"
  selectionType: SelectionGate
  selectionGate:
    requiredCount: 2
    requiredTag: "high_templar"

Phase Graph:
  1. SelectionGate: 等待玩家选择 2 个 High Templar
  2. ValidatePrecondition: 两个单位都 HasTag("high_templar")
  3. 计算 midpoint = (pos_a + pos_b) / 2
  4. DestroyEntity(unit_a)
  5. DestroyEntity(unit_b)
  6. CreateUnit("archon", position=midpoint)
  7. ApplyEffect: archon.shields = 350
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| SelectionGate | P3 | 多步骤选择交互，等待玩家选择多个特定单位 |
| CreateUnit in Phase Graph | P2 | 在技能执行中动态创建新单位 |

## 参考案例
- **StarCraft 2**: 两个高阶圣堂武士合并成执政官
- **Warcraft 3**: 天神下凡（英雄合体技）
