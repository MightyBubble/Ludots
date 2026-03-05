# Mod 运行时唯一真相与收束准则

本文定义 Mod 相关链路在运行时与工具链中的统一规则，目标是消除隐式回退、路径双真相与配置歧义。读完后可以直接据此检查主线是否满足“配置表达产品意图、代码面向生产”的要求。

## 1 Mod 装载规则（无回退）

### 1.1 `mod.json.main` 是唯一 DLL 入口

- 当 `main` 存在时，加载器只认该路径。
- 当 `main` 缺失时，视为资源型 Mod，不执行程序集扫描。
- 不允许根据 Debug/Release、时间戳或候选列表自动改写目标 DLL。

参考实现：`src/Core/Modding/ModLoader.cs`

### 1.2 统一产物目录

- 内置 Mod 构建产物统一到 `bin/net8.0/`。
- `mod.json.main` 必须与该目录对齐。
- 不允许同一 Mod 同时依赖 `bin/Release/net8.0` 与 `bin/net8.0` 两套规则。

参考实现：`src/Mods/Directory.Build.props`

## 2 启动器/CLI/工具链一致性

### 2.1 目录语义一致

- GUI 启动器、CLI、工具命令统一围绕 `src/Mods`。
- 允许附加外部目录，但语义应与主目录一致（同样扫描 `mod.json`、同样依赖解析）。
- 禁止 `src/Mods` 与 `assets/Mods` 并行作为默认真相源。

参考实现：

- `src/Tools/ModLauncher/MainViewModel.cs`
- `src/Tools/ModLauncher/Cli/CliRunner.cs`
- `src/Tools/Ludots.Tool/Program.cs`

### 2.2 依赖闭包与顺序

- 显式选择的 Mod 集合必须补齐依赖闭包。
- 输出顺序必须可复现（依赖拓扑 + 稳定排序）。
- `game.json` 写回路径需保持稳定且可读。

## 3 配置 ID 规则（配置即意图）

### 3.1 统一字段与类型

- ArrayById 配置统一使用 `id` 字段。
- `id` 必须是字符串。
- 禁止大小写猜测与字段名回退（例如 `Id`→`id` 的隐式兼容）。

参考实现：

- `src/Core/Config/ConfigMerger.cs`
- `src/Core/Config/ConfigCatalogEntry.cs`
- `src/Core/Config/ConfigCatalogLoader.cs`

### 3.2 运行时与 Bridge 同步

- Runtime Loader 与 Bridge 合并逻辑必须遵循同一 ID 语义。
- 若某子系统需要数值 ID，应在边界层进行显式转换，不改变配置层字符串真相。

参考实现：

- `src/Core/Presentation/Config/PerformerDefinitionConfigLoader.cs`
- `src/Tools/Ludots.Editor.Bridge/Program.cs`

## 4 容量边界必须可观测

### 4.1 丢弃路径显式统计

- `ActiveEffectContainer.Add()` 失败必须计数。
- Listener 收集/注册截断必须计数。
- 事件总线溢出必须计入预算。

参考实现：

- `src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs`
- `src/Core/Gameplay/GAS/Systems/EffectPhaseExecutor.cs`
- `src/Core/Gameplay/GAS/Systems/GameplayEventDispatchSystem.cs`
- `src/Core/Gameplay/GAS/GasBudget.cs`

### 4.2 预算字段可用于测试与报告

- 预算字段应可由生产报告与自动化测试直接断言。
- 禁止通过忽略测试掩盖容量问题。

参考实现：`src/Tests/GasTests/RootBudgetTests.cs`

## 5 主线收束检查清单

- [ ] ModLoader 不存在 DLL 回退分支。
- [ ] `mod.json.main` 全量指向 `bin/net8.0/*.dll`。
- [ ] GUI/CLI/Tool 默认目录一致且依赖闭包一致。
- [ ] `config_catalog.json` 的 ArrayById 条目使用 `IdField: "id"`。
- [ ] 关键配置文件 `id` 字段为字符串。
- [ ] GasBudget 包含主要丢弃路径统计。
- [ ] 生产报告与演示日志相关测试可直接通过。

## 6 相关文档

- Mod 架构与配置系统：见 [02_mod_architecture.md](02_mod_architecture.md)
- CLI 启动与调试指南：见 [04_cli_guide.md](04_cli_guide.md)
- ConfigPipeline 合并管线：见 [07_config_pipeline.md](07_config_pipeline.md)
- 数据配置合并策略：见 [12_config_data_merge_best_practices.md](12_config_data_merge_best_practices.md)
