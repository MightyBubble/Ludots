# U3: Destructible

## 机制描述
技能可以破坏环境中的可破坏物体（箱子、墙壁等）。

## 交互层设计
- **Input**: Down
- **Selection**: Point / Area
- **Resolution**: ContextScored（自动检测范围内可破坏物）

## 实现要点
```
Phase Graph:
  1. QueryRadius(center, radius)
  2. QueryFilterLayer(Destructible)  // 筛选可破坏物
  3. FanOutApplyEffect(destroy_effect)

// 可破坏物 entity:
Components:
  Tag: "destructible"
  Health: 100
  OnDeath:
    SpawnLoot(loot_table)
    PlayEffect(destruction_vfx)
    DestroyEntity(self)
```
- 可破坏物通过 Tag 或 Layer 标识
- 破坏后可掉落物品、触发事件

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| QueryFilterLayer | P1 | Phase Graph 支持按 Layer/Tag 筛选查询结果 |

## 参考案例
- **Diablo**: 破坏箱子掉落物品
- **Zelda**: 破坏罐子获得道具
- **Overwatch**: 破坏环境物体
