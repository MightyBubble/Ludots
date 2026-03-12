# R3: Multi-Unit Micro

## 机制描述
SC2 式多单位微操：框选多个单位后对选中单位集体发送指令。

## 交互层设计
- **Input**: Down
- **Selection**: Entities (框选多单位)
- **Resolution**: Explicit / ContextScored

## 实现要点
```
// 已有系统支持
OrderSelectionType.Entities:
  → SelectionGroupBuffer → 框选多单位
  → Order 对每个选中单位发送副本

InputOrderMapping:
  actionId: "MoveOrder"
  selectionType: Entities
  interactionMode: Explicit
```
- SelectionGroupBuffer 维护当前选中的 Entity 集合
- 指令（Attack/Move/Stop）广播给所有选中单位
- 每个单位独立接收 Order 副本执行

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| 无新增（已有框选+多单位指令支持） | — | — |

## 参考案例
- **StarCraft 2**: 框选单位微操
- **Age of Empires**: 多单位编队操作
- **Company of Heroes**: 分队微操
