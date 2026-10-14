# Q7: Combo Count Execute

## 机制描述
当连击数达到特定阈值时，可以执行连击终结技。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Precondition**: `self.combo_meter >= threshold`

## 实现要点
目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
AbilityActivationRequire:
  AttributePrecondition:
    self.combo_meter >= 5

Phase Graph:
  1. ApplyEffect(combo_finisher_damage * combo_meter)
  2. ModifyAttribute(self, combo_meter, 0)  // Reset
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Attribute precondition | P0 | 复用 G8 需求，支持 Ability 级别的 Attribute 门控 |

## 参考案例
- **Devil May Cry**: 连击数越高，终结技伤害越高
- **God of War**: 连击条满后可以释放终结技
- **Bayonetta**: 连击终结技
