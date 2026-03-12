# Q5: Parry Execute

## 机制描述
成功弹反敌人攻击后，可以执行弹反处决技能。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: ContextScored
- **Precondition**: `self HasTag("parry_success")`

## 实现要点
```
AbilityActivationRequire:
  RequireTags: ["parry_success"]  // 自身必须有弹反成功 Tag

ContextGroup:
  candidate: "ParryExecute"
    precondition: self HasTag("parry_success")
    score: 150

Phase Graph:
  1. ApplyEffect(riposte_damage)
  2. RemoveTag(self, "parry_success")
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| AbilityActivationBlockTags.RequiredAll | ✅ 已有 | 对应现有 RequiredAll 字段，支持 Tag 门控 |

## 参考案例
- **Sekiro**: 弹反后可以执行处决
- **Dark Souls**: 弹反后可以暴击
- **For Honor**: 弹反后可以处决
