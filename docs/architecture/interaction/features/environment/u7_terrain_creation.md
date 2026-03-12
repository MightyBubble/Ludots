# U7: Terrain Creation

## 机制描述
技能可以创造地形（墙壁、障碍物等），影响寻路和战斗。

## 交互层设计
- **Input**: Down
- **Selection**: Point / Line
- **Resolution**: Explicit

## 实现要点
```
Phase Graph:
  1. SelectionGate: 选择墙壁起点
  2. SelectionGate: 选择墙壁终点
  3. 计算墙壁段数: segments = distance / segment_length
  4. Loop: CreateUnit("wall_segment", position=interpolate(start, end, i))

// 墙壁 entity:
Components:
  Tag: "terrain_wall"
  NavigationBlocker: true  // 阻挡寻路
  Collision: solid
  Duration: 300 ticks  // 持续时间后自动消失
```
- 墙壁 entity 有 NavigationBlocker 组件 → 影响寻路系统
- 墙壁可以被破坏或有持续时间限制

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Navigation blocker for spawned walls | P2 | 动态创建的墙壁影响寻路系统 |
| CreateUnit in Phase Graph | P2 | 技能执行中动态创建 entity |

## 参考案例
- **Overwatch Mei**: 冰墙创造
- **Fortnite**: 建造墙壁
- **Dota 2 Earthshaker**: 地形创造
