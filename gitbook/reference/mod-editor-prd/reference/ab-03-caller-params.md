# ab-03 reference · CallerParams 参数池

> 现状参考。第一性需求见 [ab-03 PRD](../prd/ab-03-caller-params.md)；配置说明见 [ab-03 配置说明](../config/ab-03-caller-params.md)。

## 1. 现状快照

- 池结构 MAX_SETS=4，内联 _p0.._p3 存储（每组合入 blittable 结构）；item 以 callerParamsIdx 引用，0xFF=无。
- 触发效果条目：hasCp = 有池 && idx≠0xFF && idx<Count，取池组入 EffectRequest.CallerParams；越界索引静默按"无参数"处理（无独立报错）。
- 空间参数注入：有目标位置 TryAdd TargetPosX/Y、有原点 TryAdd TargetOriginX/Y；追加失败整技能 PreconditionFailed，错误不指明是池余位不足。
- 合并两条路径：实体路径优先读创建时预合并的 EffectConfigParams 组件；请求路径 MergeFrom 按"同键覆盖、容量满静默丢"。
- 仓库现状：全部 abilities.json 无 callerParams 使用（能力已接通、无真实消费者）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 池结构与上限 | src/Core/Gameplay/GAS/Components/AbilityExecComponents.cs:149-182 |
| 池编译（组数/单组上限） | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:437-489 |
| 条目 callerParamsIdx 编译 | AbilityExecLoader.cs:399-403 |
| FireEffectItem 取池参数 | src/Core/Gameplay/GAS/Systems/AbilityExecSystem.cs:1078-1090 |
| 空间参数注入 | AbilityExecSystem.cs:1185-1212 |
| 请求路径合并 MergeFrom | src/Core/Gameplay/GAS/Components/EffectConfigParams.cs:193-221 |
| 实体路径预合并优先 | src/Core/Gameplay/GAS/ConfigParamsMerger.cs:18-29 |
| 单组参数上限常量 | GasConstants EFFECT_CONFIG_PARAMS_MAX（见 facts 页） |

**相关文档**：[ab-03 PRD](../prd/ab-03-caller-params.md) · [ab-02 reference](ab-02-exec-timeline.md)
