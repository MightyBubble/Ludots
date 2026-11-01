# R2: Companion Skill

## 机制描述
玩家通过输入指令切换同伴的行为模式或命令同伴使用特定技能。

## 交互层设计
- **Input**: Down
- **Selection**: None
- **Resolution**: Explicit
- **Order Actor**: companion_entity（非 local player）

## 实现要点
目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
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
