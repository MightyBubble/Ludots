# fx-16 reference · 堆叠

> 现状参考。第一性需求见 [fx-15 PRD](../prd/fx-12-stack.md)；配置说明见 [fx-15 配置说明](../config/fx-12-stack.md)。

## 1. 现状快照

- EffectStack：三策略 RefreshDuration/AddDuration/KeepDuration + 两溢出 RejectNew/RemoveOldest；TryAddStack 在 Limit>0 且 Count≥Limit 时：RemoveOldest 换新不增 Count、RejectNew 返 false。
- 编译：limit/policy/overflowPolicy 全必填；limit 无正值校验——0/负值=无上限意外放行（todo/effect.md E3）。
- 应用：仅声明堆叠策略且非 Instant 的效果参与合并；按模板 id 在目标容器找现有效果；策略作用于 RemainingTicks 且 ExpiresAtTick=0 下帧重算；新实体首应用 Count=1。
- 授予联动：差量 Compute(new)−Compute(old) 经标签操作落地，失败先回滚堆叠/效果实体再上抛；三对方法全量回滚；公式 Fixed/Linear/LinearPlusBase 可用，GraphProgram 在 loader 直接拒（其后图解析为死代码，E4）；标签层数溢出抛错并计预算。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 策略与溢出定义 | src/Core/Gameplay/GAS/Components/EffectStack.cs:6-27 |
| TryAddStack | EffectStack.cs:46-59 |
| 三字段必填（无正值校验） | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:2038-2070 |
| 合并与策略作用点 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1624-1728, 1641-1652 |
| 新实体首层 | EffectProposalProcessingSystem.cs:1719-1728 |
| 授予差量与回滚顺序 | EffectProposalProcessingSystem.cs:1673-1678 |
| 差量计算 | src/Core/Gameplay/GAS/EffectTagContributionHelper.cs:84-125 |
| 三对方法全量回滚 | EffectTagContributionHelper.cs:13-200 |
| 标签层数溢出 | EffectTagContributionHelper.cs:143-147, 191-195 |
| 图公式拒绝（死代码起点） | EffectTemplateLoader.cs:1988-1992 |

**相关文档**：[fx-15 PRD](../prd/fx-12-stack.md) · [fx-03 reference](fx-01-pipeline.md)
