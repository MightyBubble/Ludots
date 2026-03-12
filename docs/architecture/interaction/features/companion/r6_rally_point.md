# R6: Rally Point

## 机制描述
设置集结点，新生产的单位自动向集结点移动。

## 交互层设计
- **Input**: Down
- **Selection**: Point (世界坐标)
- **Resolution**: Explicit

## 实现要点
```
InputOrderMapping:
  actionId: "SetRallyPoint"
  selectionType: Point
  interactionMode: Explicit

OrderSubmitter:
  Order.Actor = building_entity  // 建筑或同伴基地
  Order.Args.Spatial = clicked_world_position

Phase Graph:
  1. SetBlackboard(building, "rally_point", Order.Args.Spatial)
  2. UI: ShowRallyPointMarker(position)

// 单位生产完成时:
SpawnPipeline:
  OnUnitCreated:
    if building.Blackboard.HasKey("rally_point"):
      IssueOrder(new_unit, Move, rally_point)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Input focus / actor routing | P2 | 复用 K7 |
| Blackboard 集结点读写 | P1 | 建筑/基地 entity 上的 Blackboard 键值 |

## 参考案例
- **StarCraft**: 右键设置集结点
- **Age of Empires**: 兵营集结点旗帜
- **Total War**: 部队集结位置
