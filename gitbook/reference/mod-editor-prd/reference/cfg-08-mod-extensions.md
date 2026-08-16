# cfg-08 reference · mod 代码扩展面

> 现状参考。第一性需求见 [cfg-08 PRD](../prd/cfg-08-mod-extensions.md)；配置说明见 [cfg-08 配置说明](../config/cfg-08-mod-extensions.md)；目标实现见 [cfg-08 runtime spec](../spec-runtime/cfg-08-mod-extensions.md)。

## 1. 现状快照

- 扩展面已在 main（合并提交 9e05ca07f5）：入口上下文带 `Extensions` 门面，Gas 组可注册内建处理器与图 op（两种重载，其一支持固定寄存器），Presentation 组可注册表现器命令与行为。
- 注册只在 `IMod.OnLoad` 窗口；扩展枢纽在配置编译前冻结，冻结后注册抛错；键单主声明，重复或撞名抛错。
- 四个运行时注册表分别承接四类注册；配置编译期解析扩展键引用，未注册即失败。
- 配套四个可玩 showcase 与注册合同测试（5 项，含窗口/冻结/重复键）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 门面接口（Gas / Presentation 两组注册面）与可变枢纽、冻结语义 | src/Core/Modding/ModExtensionHub.cs:5-60 |
| 注册转发（四个注册面 → 各运行时注册表） | src/Core/Modding/ModExtensionHub.cs:145 起 |
| 入口上下文暴露 Extensions | src/Core/Modding/IModContext.cs:17 |
| 内建处理器注册表 | src/Core/Gameplay/GAS/BuiltinHandlerRegistry.cs |
| 图 op 执行表 | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs |
| 表现命令/行为注册表 | src/Core/Presentation/Presenters/PerformerExtensionRegistries.cs |
| 合同正本（四扩展面与铁律 SSOT） | gitbook/architecture/mod-extensible-runtime.md |
| 注册合同测试 | src/Tests/PresentationTests（ModExtensionRegistrationTests） |
| 处理器注册 + 数据引用完整示例 | mods/showcases/capability_standard/CapabilityStandardEffectPresetTypeCodeShowcaseMod/CapabilityStandardEffectPresetTypeCodeShowcaseModEntry.cs |

**相关文档**：[cfg-08 prd](../prd/cfg-08-mod-extensions.md) · [cfg-08 spec](../spec-runtime/cfg-08-mod-extensions.md) · [cfg-01 reference](cfg-01-mod-manifest.md)
