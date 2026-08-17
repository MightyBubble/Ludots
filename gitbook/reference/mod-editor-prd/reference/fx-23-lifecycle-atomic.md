# fx-23 reference · 生命周期原子操作

> 现状参考。第一性需求见 [fx-23 PRD](../prd/fx-23-lifecycle-atomic.md)；配置说明见 [fx-23 配置说明](../config/fx-23-lifecycle-atomic.md)。

## 1. 现状快照

- loader：DeployConsumeSource 必须 Instant；`_ep.targetEntityTemplate`（EntityTemplate）>0、`_ep.lifecycleAttributeValueSource` ∈ Base/Current、至少 1 条 `_ep.lifecycleAttributeN`（容量 4），三者缺一即抛错并报键名。
- 运行链：preset 默认 OnApply 图 `Graph.Lifecycle.DeployConsumeSource`（Begin + 六个 InvokeBuiltin）；BeginLifecycleTransaction 快照源实体、解析模板与放置点、按配置组装事务态；执行器依序跑六 op，失败回滚已物化目标。
- 六个生命周期内建（MaterializeTemplate…ConsumeEntity）全部注册为 Unsupported(Lifecycle)：预设默认图无法通过 FinalizeAll 计划编译；现有生命周期测试全部绕过 FinalizeAll 直连执行器/执行相位验证。
- 仓库无 mod 直接使用 DeployConsumeSource 效果条目；参数三件套现货在 GraphOps 展示 mod。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| preset 组合与三件必配 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:504-519 |
| 生命周期参数检查 | EffectTemplateLoader.cs:1407-1431 |
| 事务开始 | src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs:431-496 |
| 六步 op 序列 | src/Core/Gameplay/Lifecycle/RuntimeEntityLifecycleTransactionExecutor.cs:12-22 |
| 执行器与回滚 | RuntimeEntityLifecycleTransactionExecutor.cs:27-64 |
| 六内建 Unsupported 注册 | src/Core/Gameplay/Lifecycle/EntityLifecycleBuiltinHandlers.cs:11-16 |
| 预设默认图 | assets/GAS/preset_types.json:141-148 |
| 默认图本体 | assets/GAS/graphs.json（Graph.Lifecycle.DeployConsumeSource） |
| 直连验证测试 | src/Tests/GasTests/Integration/LifecycleArchitectureTests.cs:44-320 |

**相关文档**：[fx-23 PRD](../prd/fx-23-lifecycle-atomic.md) · [fx-23 配置说明](../config/fx-23-lifecycle-atomic.md)
