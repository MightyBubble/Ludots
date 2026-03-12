# R4: Transport Load

## 机制描述
将单位装载到运输载具中，载具可以运输多个单位。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (transport → target)
- **Resolution**: Explicit
- **双实体交互**: transport entity 与 cargo entity

## 实现要点
```
InputOrderMapping:
  actionId: "LoadUnit"
  selectionType: Entity
  interactionMode: Explicit

Phase Graph:
  1. ValidatePrecondition: transport HasComponent("TransportCapacity")
  2. ValidatePrecondition: transport.cargo_count < transport.max_capacity
  3. ApplyEffect: AddToCargoList(transport, target)
  4. ApplyEffect: SetVisible(target, false)
  5. ApplyEffect: AttachPosition(target, transport)
```
- 卸载指令: 反向操作，恢复 target 的独立存在
- 载具组件: `TransportCapacity { MaxSlots, CargoBuffer }`

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Input focus / actor routing | P2 | 复用 K7 |
| TransportCapacity 组件 | P2 | 维护载具容量和货物列表 |

## 参考案例
- **StarCraft**: 运输舰装载陆战队
- **Warcraft 3**: 飞行运输装载地面单位
- **Homeworld**: 运输舰载入战斗机
