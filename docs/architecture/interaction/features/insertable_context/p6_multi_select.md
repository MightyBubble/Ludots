# P6: 多次选取

## 交互层

玩家在技能执行过程中，需要进行多次目标选择（如分阶段选择不同目标组、连续标记多个目标）。

典型场景：
- 分阶段选择不同目标组（第一组施加 Buff，第二组施加 Debuff）
- 连续标记多个目标（如标记 3 个敌人后依次攻击）
- 多目标分配技能（如治疗多个友方，每次选择一个）

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: SelectionGate → 选第一组目标 (MaxCount=32)
    → `src/Core/Input/Selection/GasSelectionResponseSystem.cs`：按 `SelectionRuleRegistry` 解析点选 / 半径选择结果
  Item[1]: EffectSignal → process_group_1
  Item[2]: SelectionGate → 选第二组目标
  Item[3]: EffectSignal → process_group_2
```

执行流程：
1. 第一个 `SelectionGate` 暂停执行，等待玩家选择第一组目标
2. `GasSelectionResponseSystem` 根据 `SelectionRuleRegistry` 解析选择结果（点选/半径选择）
3. 第一个 `EffectSignal` 处理第一组目标
4. 第二个 `SelectionGate` 暂停执行，等待玩家选择第二组目标
5. 第二个 `EffectSignal` 处理第二组目标

## 依赖组件

- `SelectionGate` — AbilityExecSpec 中的 Gate 类型，支持 `MaxCount` 参数
- `src/Core/Input/Selection/GasSelectionResponseSystem.cs` — 处理选择输入
- `SelectionRuleRegistry` — 解析点选/半径选择规则
- `SelectionResponse` — 存储每次选择的实体列表

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
- `docs/architecture/interaction/features/insertable_context/p1_select_extra_target.md` — 选择额外目标（单次选择场景）
