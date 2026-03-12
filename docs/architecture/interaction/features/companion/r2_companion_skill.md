# R2: Companion Skill

## 机制描述
玩家通过输入指令切换同伴的行为模式或命令同伴使用特定技能。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Order Actor**: companion_entity（非 local player）

## 实现要点
```
InputOrderMapping:
  actionId: "CompanionSkill"
  selectionType: None
  interactionMode: Explicit

OrderSubmitter:
  Order.Actor = companion_entity
  Order.Type = "UseSkill"
  Order.Args.SkillId = "companion_special_skill"
```
- 切换同伴行为模式时，设置 companion Blackboard 上的模式标志
- 模式枚举: Aggressive / Defensive / Support / Hold

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Input focus / actor routing | P2 | 复用 K7，支持将 Order 路由到指定 Actor |

## 参考案例
- **Dragon Age**: 暂停发布技能指令
- **Phantom Dust**: 同伴技能切换
- **FFXII**: 编程式伽玛比特指令
