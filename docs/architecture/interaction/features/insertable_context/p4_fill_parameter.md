# P4: 填写数值参数

## 交互层

玩家在技能执行前或执行中，需要填写数值参数（如分配资源数量、设置持续时间、调整强度比例）。

典型场景：
- 分配治疗量到多个目标（滑块调整比例）
- 设置技能持续时间（消耗对应资源）
- 调整伤害/防御比例（攻守转换类技能）

## Ludots 实现

```
AbilityExecSpec:
  Item[0]: InputGate → 等待 UI 输入
    → UI 显示滑块/数字选择
    → InputResponse.PayloadA = user_value
  Item[1]: EffectSignal → apply_with_value
    → ConfigParams: value = InputResponse.PayloadA
```

执行流程：
1. `AbilityExecSystem` 执行到 `InputGate`，暂停执行
2. UI 显示数值输入界面（滑块/数字输入框）
3. 玩家输入数值后，`GasInputResponseSystem` 生成 `InputResponse`，`PayloadA` 存储数值
4. 后续 `EffectSignal` 从 `ConfigParams` 读取数值执行效果

## 依赖组件

- `InputGate` — AbilityExecSpec 中的 Gate 类型
- `InputResponse.PayloadA` — 存储玩家输入的数值
- `src/Core/Input/Interaction/GasInputResponseSystem.cs` — 处理输入响应
- `EffectSignal.ConfigParams` — 传递数值参数到效果执行

## 新增需求

无。已有组件完全支持此机制。

## 相关文档

- `docs/architecture/interaction/features/08_response_window_and_context.md` — 响应窗口总览
