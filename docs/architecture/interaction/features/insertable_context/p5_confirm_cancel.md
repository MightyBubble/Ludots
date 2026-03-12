# P5: 确认/取消高代价技能

## 交互层

玩家在释放高代价技能（如大招、消耗大量资源的技能）前，需要进行二次确认，防止误操作。

典型场景：
- 大招释放前的确认提示
- 消耗稀有资源的技能确认
- 不可逆操作的确认（如牺牲单位）

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: InputGate → 等待确认
    → `src/Core/Input/Interaction/GasInputResponseSystem.cs`: ConfirmActionId press → confirm
    → CancelActionId press → cancel (ability interrupted)
  Item[1]: if confirmed → EffectSignal → execute_nuke
```

执行流程：
1. `AbilityExecSystem` 执行到 `InputGate`，暂停执行
2. UI 显示确认/取消提示
3. 玩家按下确认键 → `GasInputResponseSystem` 生成确认 Response，继续执行
4. 玩家按下取消键 → 技能中断，不执行后续 EffectSignal

## 依赖组件

- `InputGate` — AbilityExecSpec 中的 Gate 类型
- `src/Core/Input/Interaction/GasInputResponseSystem.cs` — 处理确认/取消输入
- `ConfirmActionId` / `CancelActionId` — 输入映射

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
