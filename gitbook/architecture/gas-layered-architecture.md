# GAS 分层架构

本页描述 Ludots 当前 GAS 的正式分层和边界。

## 1 宏观分层

GAS 的宏观分层由 `SystemGroup` 固化，重点 phase 包括：

- `AbilityActivation`
- `EffectProcessing`
- `AttributeCalculation`
- `DeferredTriggerCollection`

这保证了执行顺序可预期，避免系统靠隐式顺序耦合。

## 2 核心链路

- 输入与能力激活先转成 `EffectRequest`
- EffectProcessing 负责 proposal、response、resolve、apply 和 lifetime
- AttributeCalculation 统一把属性缓冲落到目标层
- DeferredTriggerCollection 处理延迟触发的收束逻辑

## 3 Sink 边界

跨层写入优先通过 Sink：

- `AttributeSinkRegistry` 负责注册和冻结 sink ID
- `AttributeBindingSystem` 负责把属性缓冲按绑定落地
- 类型转换、时钟域对齐和写入策略应集中在 sink，而不是扩散到 gameplay 热路径

## 4 约束

- 结构变更集中在明确阶段或回放队列中执行
- effect phase 不依赖某个 sink 的隐式执行顺序
- gameplay 逻辑不直接越层写物理、UI 或表现状态

## 5 深度材料

- 仓库深度版：`docs/architecture/gas_layered_architecture.md`
- Input / spawn target 基建：`gitbook/architecture/input-order-and-spawn-target.md`
- 相关实现：`src/Core/Gameplay/GAS/Bindings/`
