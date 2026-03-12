# P7: 条件分支选择

## 交互层

玩家在技能执行过程中，需要选择不同的执行分支（如选择不同的效果路径、根据条件选择不同的后续行为）。

典型场景：
- 技能可以选择攻击/防御/辅助三种模式
- 根据战场情况选择不同的效果组合
- 多分支技能树（如选择升级路径）

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: InputGate → UI 显示选项列表
    → ResponseChainUiState.AllowedOrderTypeIds 列出可选项
    → 玩家选择 → InputResponse.PayloadA = selected_option_id
  Item[1]: EffectSignal → Phase Graph:
    1. ReadBlackboardInt(selected_option)
    2. CompareEqInt(option, 1) → branch_A
    3. CompareEqInt(option, 2) → branch_B
    4. ApplyEffectTemplate(branch_result)
```

执行流程：
1. `InputGate` 暂停执行，UI 通过 `ResponseChainUiState.AllowedOrderTypeIds` 显示可选项
2. 玩家选择后，`GasInputResponseSystem` 生成 `InputResponse`，`PayloadA` 存储选项 ID
3. 后续 `EffectSignal` 执行 Phase Graph：
   - 从 Blackboard 读取选项 ID
   - 使用 `CompareEqInt` 等条件节点判断分支
   - 根据分支结果应用不同的 EffectTemplate

## 依赖组件

- `InputGate` — AbilityExecSpec 中的 Gate 类型
- `ResponseChainUiState.AllowedOrderTypeIds` — 列出可选项
- `InputResponse.PayloadA` — 存储玩家选择的选项 ID
- `src/Core/Input/Interaction/GasInputResponseSystem.cs` — 处理输入响应
- Phase Graph 条件节点 — `ReadBlackboardInt`, `CompareEqInt`, `ApplyEffectTemplate`

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
- `docs/architecture/interaction/features/insertable_context/p2_choose_effect_variant.md` — 选择效果变体（相关机制）
