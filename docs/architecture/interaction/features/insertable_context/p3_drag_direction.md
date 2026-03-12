# P3: 拖拽确定二阶段方向

## 交互层

玩家在技能执行过程中，需要通过拖拽或二次点击确定方向/落点（如抓取友方后投掷的方向、位移技能的二段方向）。

典型场景：
- 抓取友方英雄后，拖拽确定投掷方向和距离
- 二段位移技能（如李青 Q），第一段命中后选择是否跟进
- 钩锁类技能，命中后选择拉自己还是拉目标

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: EffectSignal → execute_phase_1 (如: 抓住友方)
  Item[1]: SelectionGate → 等待玩家选择方向/落点
  Item[2]: EffectSignal → execute_phase_2 (如: 投出友方到选定方向)
```

执行流程：
1. 第一个 `EffectSignal` 执行初始效果（抓取、命中等）
2. `SelectionGate` 暂停执行，等待玩家输入方向/位置
3. `GasSelectionResponseSystem` 捕获玩家的拖拽/点击输入
4. 第二个 `EffectSignal` 使用 `SelectionResponse` 中的位置/方向数据执行后续效果

## 依赖组件

- `SelectionGate` — AbilityExecSpec 中的 Gate 类型
- `src/Core/Input/Selection/GasSelectionResponseSystem.cs` — 处理选择输入
- `SelectionResponse` — 存储方向/位置数据

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
- `docs/architecture/interaction/features/insertable_context/p1_select_extra_target.md` — 选择额外目标（相关机制）
