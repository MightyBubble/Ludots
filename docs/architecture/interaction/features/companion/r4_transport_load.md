# R4: Transport Load

## 机制描述
将单位装载到运输载具中，载具可以运输多个单位。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (transport → target)
- **Resolution**: Explicit
- **双实体交互**: transport entity 与 cargo entity

## 实现要点
目标规则图是 `graph.aim.rule.unit_or_ground`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
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
