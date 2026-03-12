# R1: Companion Attack

## 机制描述
玩家通过输入指令控制同伴攻击指定目标。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: Explicit
- **Order Actor**: companion_entity（非 local player）

## 实现要点
```
InputOrderMapping:
  actionId: "CompanionAttack"
  selectionType: Entity
  interactionMode: Explicit

OrderSubmitter:
  Order.Actor = companion_entity  // 不是 local player
  Order.Target = selected_enemy
  Order.Type = "Attack"
```
- 需要: Order 可以指定 Actor 为非 local player 的 entity（已有 Order.Actor 字段）

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Input focus / actor routing | P2 | 复用 K7，支持将 Order 路由到指定 Actor |

## 参考案例
- **FFXV**: 指令同伴发动联合技
- **RE5**: 指令 AI 同伴攻击目标
- **Divinity Original Sin**: 指挥队友攻击
