# Q8: QTE Execute

## 机制描述
执行处决技能时，需要通过 QTE（Quick Time Event）按键序列，成功则造成高伤害，失败则伤害降低。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Precondition**: InputGate sequence

## 实现要点
```
AbilityExecSpec:
  Item[0]:
    Animation: "execute_start"
    UI: ShowPrompt("Press X")

  Item[1]:
    InputGate:
      expectedKey: "X"
      deadline: 30 ticks

  Item[2]:
    Branch:
      if correct → ApplyEffect(heavy_damage)
      if timeout → ApplyEffect(reduced_damage)

  Item[3]:
    UI: ShowPrompt("Press O")
    InputGate:
      expectedKey: "O"
      deadline: 30 ticks

  Item[4]:
    Branch:
      if correct → ApplyEffect(final_blow)
      if timeout → ApplyEffect(weak_blow)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| InputGate with deadline | P1 | 支持等待特定按键输入，超时则失败 |
| Branch based on InputGate result | P1 | 根据 QTE 成功/失败分支执行不同效果 |

## 参考案例
- **God of War**: 处决时需要按提示按键
- **Resident Evil 4**: QTE 处决
- **Asura's Wrath**: 大量 QTE 战斗
