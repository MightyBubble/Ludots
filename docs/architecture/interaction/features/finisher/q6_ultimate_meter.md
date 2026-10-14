# Q6: Ultimate Meter

## 机制描述
当大招仪表充满时，可以释放终极技能。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Precondition**: `self.ultimate_charge >= 100`

## 实现要点
目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
AbilityActivationRequire:
  AttributePrecondition:
    self.ultimate_charge >= 100

Phase Graph:
  1. ModifyAttribute(self, ultimate_charge, -100)
  2. ApplyEffect(ultimate_effect)
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Attribute precondition | P0 | 复用 G8 需求，支持 Ability 级别的 Attribute 门控 |

## 参考案例
- **Overwatch**: 大招仪表充满后按 Q 释放
- **Valorant**: 终极技能点数充满后释放
- **Apex Legends**: 大招充能完成后释放
