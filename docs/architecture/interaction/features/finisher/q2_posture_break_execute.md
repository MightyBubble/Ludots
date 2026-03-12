# Q2: Posture Break Execute

## 机制描述
当目标的 Posture 值被打满破防时，可以执行特殊处决技能。

## 交互层设计
- **Input**: Down
- **Selection**: Unit (目标敌人)
- **Resolution**: ContextScored
- **Precondition**: `target HasTag("posture_broken")`

## 实现要点
```
AbilityActivationRequire:
  RequireTags: ["posture_broken"]  // 目标必须有此 Tag

ContextGroup:
  candidate: "PostureExecute"
    precondition: target HasTag("posture_broken")
    score: 100
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| AbilityActivationRequireTags | P0 | 复用 G 需求，支持 Tag 门控 |

## 参考案例
- **Sekiro**: Posture 条满后可以执行忍杀
- **Sifu**: 结构值破防后可以处决
