# U5: Terrain Combat Variant

## 机制描述
根据地形类型（水面、高地、森林等）触发不同的战斗效果或技能变体。

## 交互层设计
- **Input**: Down
- **Selection**: 根据技能决定
- **Resolution**: ContextScored（根据地形评分）

## 实现要点
```
ContextGroup:
  candidate: "WaterSlash"
    precondition: self.terrain_type == "water"
    score: 120
    argsTemplate: { abilityId: "water_slash", damage_bonus: 1.5 }

  candidate: "NormalSlash"
    precondition: true
    score: 100
    argsTemplate: { abilityId: "normal_slash" }

// 地形检测:
TerrainQuerySystem:
  OnEntityMove:
    terrain_type = QueryTerrainType(entity.position)
    SetAttribute(entity, "terrain_type", terrain_type)
```
- 地形类型通过 Attribute 或 Tag 标识
- ContextGroup 根据地形类型评分选择技能变体

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Terrain type query | P2 | 查询当前位置的地形类型 |

## 参考案例
- **Fire Emblem**: 地形影响命中和回避
- **XCOM**: 地形掩体系统
- **Divinity Original Sin**: 地形元素交互
