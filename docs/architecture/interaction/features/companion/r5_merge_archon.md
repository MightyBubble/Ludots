# R5: Merge / Archon

## 机制描述
选择两个特定单位合并成一个更强大的新单位（如星际 2 中的两个高阶圣堂武士合并成执政官）。

## 交互层设计
- **Input**: Down
- **Target**: TargetCollectionGate → 选 2 个特定类型单位
- **Resolution**: Explicit

## 实现要点

> ⚠️ **Architecture note**: Graph VM 不能执行结构变更（创建/销毁实体）。实体合并必须通过 BuiltinHandler → RuntimeEntitySpawnQueue 实现。

目标规则图是 `graph.aim.rule.unit_or_ground`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。

```
Phase Graph (OnCalculate):
  1. TargetCollectionGate: 等待玩家选择 2 个 High Templar
  2. ValidatePrecondition: 两个单位都 HasTag("high_templar")
  3. 计算 midpoint = (pos_a + pos_b) / 2
  4. WriteBlackboardFloat(E[effect], "merge_x", midpoint.x)
  5. WriteBlackboardFloat(E[effect], "merge_y", midpoint.y)

OnApply → BuiltinHandler: MergeUnits:
  1. ReadBlackboard(merge_x, merge_y) → position
  2. RuntimeEntitySpawnQueue.Enqueue(destroy: unit_a)
  3. RuntimeEntitySpawnQueue.Enqueue(destroy: unit_b)
  4. RuntimeEntitySpawnQueue.Enqueue(create: "archon", position=midpoint)
  5. ApplyEffect: archon.shields = 350
```

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| TargetCollectionGate | P3 | 多步骤目标集合确认，等待玩家选择多个特定单位 |
| MergeUnits BuiltinHandler | P2 | 通过 RuntimeEntitySpawnQueue 销毁源单位并创建合并单位 |

## 参考案例
- **StarCraft 2**: 两个高阶圣堂武士合并成执政官
- **Warcraft 3**: 天神下凡（英雄合体技）
