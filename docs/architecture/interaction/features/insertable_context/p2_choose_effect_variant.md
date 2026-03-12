# P2: 选择效果变体

## 交互层

玩家在技能释放前或执行中，需要选择技能的不同效果变体（如选择增强哪个基础技能、选择不同的伤害类型）。

典型场景：
- 英雄联盟中的 Karma R+Q/W/E 选择
- 技能可以选择物理/魔法/真实伤害类型
- 多形态技能的形态选择

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: InputGate @ tick 0 → 等待玩家选择 (Q/W/E哪个增强)
    → UI 显示可选效果列表
  Item[1]: EffectSignal → 根据 InputResponse.PayloadA 选择 CallerParams
    → CallerParamsIdx 指向不同配置 (4 个 CallerParams slot)
```

执行流程：
1. `AbilityExecSystem` 执行到 `InputGate`，暂停执行
2. UI 显示可选效果列表（通过 `ResponseChainUiState.AllowedOrderTypeIds`）
3. 玩家选择后，`GasInputResponseSystem` 生成 `InputResponse`，`PayloadA` 存储选择的变体 ID
4. 后续 `EffectSignal` 根据 `CallerParamsIdx` 读取对应配置执行

## 依赖组件

- `InputGate` — AbilityExecSpec 中的 Gate 类型
- `AbilityExecCallerParamsPool` — 最多 4 组配置，支持不同变体参数
- `InputResponse.PayloadA` — 存储玩家选择的变体 ID
- `src/Core/Input/Interaction/GasInputResponseSystem.cs` — 处理输入响应

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
- `docs/architecture/interaction/features/insertable_context/p7_conditional_branch.md` — 条件分支选择（相关机制）
