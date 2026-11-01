# T6: Ammo

## 机制描述
技能或攻击消耗弹药，弹药耗尽需要装填后才能继续使用。

## 交互层设计
- **Gate**: Attribute Precondition（弹药门控）
- **不影响交互层**，完全通过 Attribute + Precondition 实现

## 实现要点
目标规则图是 `graph.aim.rule.none`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
AbilityActivationRequire:
  AttributePrecondition:
    self.ammo > 0

Phase Graph:
  1. CheckPrecondition: ammo > 0
  2. Execute ability effect (射击)
  3. ModifyAttribute(self, ammo, -1)

// 装填动作:

ReloadPhaseGraph:
  1. Animation: "reload"
  2. WaitTicks: reload_time
  3. ModifyAttribute(self, ammo, max_ammo)
```
- `ammo` 当前弹匣弹药
- `total_ammo` 备用弹药（可选，用于有限弹药设计）

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Ability-level Attribute precondition | P0 | 复用 G8 需求 |
| WaitTicks in Phase Graph | P1 | 装填动作的时间延迟 |

## 参考案例
- **Valorant / CS:GO**: 弹匣弹药与装填
- **Apex Legends**: 弹药系统
- **Overwatch 士兵76**: 弹匣机制
