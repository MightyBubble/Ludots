# fx-04 reference · 八相位执行

> 现状参考。第一性需求见 [fx-04 PRD](../prd/fx-05-phases.md)；配置说明见 [fx-04 配置说明](../config/fx-05-phases.md)。

## 1. 现状快照

- 相位序 EffectPhaseId：OnPropose(0)→OnCalculate(1)→OnResolve(2)→OnHit(3)→OnApply(4)→OnPeriod(5)→OnExpire(6)→OnRemove(7)，PhaseCount=8；槽位 Pre/Main/Post。
- Main 权威：模板图优先；无模板 Main 且未 SkipMain 才回落 preset 默认处理器；绑定步上限 24=8×3；main 与 skipMain:true 互斥。
- 执行序：监听器先行收集+预检（Pre 图之前）→Pre→Main→Post→Post 后按优先级降序执行已收集监听器；监听器来源目标缓冲(scope=Target)→施法者缓冲(Source)→全局注册表；超容量抛错不丢弃。
- 随机种子 FNV 混合派生（确定性）；config 作用域包裹相位执行。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 八相位与槽定义 | src/Core/Gameplay/GAS/EffectPhaseId.cs:7-25, 31-39 |
| 绑定步上限 | src/Core/Gameplay/GAS/GasConstants.cs:62 |
| Main 回落链 | src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs:284-294 |
| 执行序（监听先行/三槽/后置监听） | EffectPhaseExecutor.cs:254-321, 274-276, 304-320 |
| 监听器执行排序 | EffectPhaseExecutor.cs:701-714 |
| 监听器三路来源 | EffectPhaseExecutor.cs:480-504 |
| 超容量抛错 | EffectPhaseExecutor.cs:511-515 |
| 确定性种子 | EffectPhaseExecutor.cs:913-933 |
| config 作用域包裹 | EffectPhaseExecutor.cs:205-252 |
| main/skipMain 互斥校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1224-1228 |

**相关文档**：[fx-04 PRD](../prd/fx-05-phases.md) · [fx-05 reference](fx-06-proposal-window.md)
