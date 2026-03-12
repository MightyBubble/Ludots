# U6: Environment Trap

## 机制描述
触发环境陷阱（地刺、火焰喷射器等），对范围内敌人造成伤害。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (陷阱实体) / Point (陷阱位置)
- **Resolution**: ContextScored（自动检测可交互陷阱）

## 实现要点
```
ContextGroup:
  candidate: "ActivateTrap"
    precondition: env entity HasTag("trap") in radius 200cm
    score: 100

Phase Graph:
  1. SelectTarget: nearest trap entity
  2. ApplyEffect(trap, "activate")
  3. trap.OnActivate:
       QueryRadius(trap.position, trap.radius)
       FanOutApplyEffect(damage)
       PlayEffect(trap_vfx)
```
- 陷阱 entity 有 Tag("trap") + 激活状态机
- 陷阱可以是一次性或可重复触发

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Environment tag scanning | P1 | 扫描附近可交互环境实体（陷阱、机关等） |

## 参考案例
- **Dark Souls**: 触发地刺陷阱
- **Zelda**: 激活机关陷阱
- **Divinity Original Sin**: 环境陷阱交互
