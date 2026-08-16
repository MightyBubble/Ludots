# cfg-05 reference · 配置管线与跨 mod 合并

> 现状参考。第一性需求见 [cfg-05 PRD](../prd/cfg-05-config-pipeline.md)；配置说明见 [cfg-05 配置说明](../config/cfg-05-config-pipeline.md)；目标实现见 [cfg-05 runtime spec](../spec-runtime/cfg-05-config-pipeline.md)。

## 1. 现状快照

- mod 加载顺序的唯一事实来源是启动计划的有序清单：引擎按 `ModLoader.LoadResolvedPlan` 逐项加载，顺序在生成期烘焙；`priority` 不参与该路径排序，仅影响启动器目录索引的展示顺序。
- 引擎本地回退路径仅在显式传入 mod 路径且无启动计划时启用（调试、无头直启）：本地拓扑排序，就绪候选按 priority 降序、发现序升序。
- 每个 mod 对每个 relativePath 从唯一根 `assets/{path}` 收集片段（单一根约定）；分片表先主文件后同根分片目录。
- 路径构造与收集集中于 ConfigSourcePaths 与 ConfigPipeline（单根 + 分片目录枚举）。
- 引擎默认片段来自仓库 `assets/Configs/`；catalog 文件自身也按 Path 同 id 跨源合并。
- 冲突报告记录片段列表、id 级赢家与删除记录；无字段级溯源。
- 文件缺失静默跳过；JSON 解析错误、条目缺 id、路径未登记、依赖问题均启动失败。
- 配置重载机制存在但不可达：重载入口支持按组重建（AI、Narrative、Quests，忽略大小写，空组=全部），唯一入口是触发器 `Config.Reload`，全仓（src/mods/tools/web）无任何发射方；重进地图走地图资产管线，不触发配置重载。

## 2. 代码锚点

### 合并实现

| 机制 | 位置 |
|---|---|
| 片段收集顺序：Core:Configs → Core: → 每个 mod 的 `assets/` 再 `assets/Configs/` | src/Core/Config/ConfigPipeline.cs:171-191；URI 构造 src/Core/Config/ConfigSourcePaths.cs:5-9 |
| 五种合并策略枚举：Replace / DeepObject / ArrayReplace / ArrayAppend / ArrayById | src/Core/Config/ConfigMergePolicy.cs:3-10 |
| 策略分发表与各策略实现 | src/Core/Config/ConfigMerger.cs:10-96 |
| 同 id 深合并核心：字段级 MergeObject、`__delete` 与 `Disabled` 删除、结果首现序 | src/Core/Config/ConfigMerger.cs:112-236 |
| loader 侧 fail-closed：路径未在 catalog 登记直接抛错 | src/Core/Config/ConfigPipeline.cs:161-169 |
| 冲突报告：片段列表、逐 id 赢家、删除记录 | src/Core/Config/ConfigConflictReport.cs:13-71 |

### 加载顺序实现

| 机制 | 位置 |
|---|---|
| 产品路径：按启动计划有序清单加载（LoadResolvedPlan）；本地回退仅在显式 modPaths 且无计划时启用 | src/Core/Engine/GameEngine.cs:451-461 |
| 顺序烘焙：依赖闭包 DFS 后序遍历，依赖按键名字母序访问、根按选择顺序；缺依赖/循环/同名歧义抛错 | src/Tools/Ludots.Launcher.Backend/LauncherService.cs:729-777 |
| priority 的全部现实作用：目录索引展示排序 | src/Tools/Ludots.Launcher.Backend/LauncherService.cs:1303 附近 |
| 本地回退排序：拓扑排序 + priority 降序 + 发现序升序 | src/Core/Modding/DependencyResolver.cs:82-136 |
| 依赖版本范围语法：`^ ~ >= <= > < =` 与 `*` | src/Core/Modding/DependencyResolver.cs:196-262 |
| mod 发现：字母序深度优先，遇含 mod.json 的目录不再下钻，跳过 bin/obj | src/Core/Modding/ModDiscovery.cs:68-108 |

### 配置重载实现

| 机制 | 位置 |
|---|---|
| 重载入口（重载 catalog 后按组重建：AI / Narrative / Quests，空组=全部） | src/Core/Engine/GameEngine.cs:587 起 |
| 唯一触发器 Config.Reload（上下文键 ConfigGroup / ConfigRelativePath），注册处 | src/Core/Config/ReloadConfigTrigger.cs；src/Core/Engine/GameEngine.cs:527 |

### 引擎接线与特例

| 机制 | 位置 |
|---|---|
| 引擎初始化建管线、合并 game.json、载入 catalog | src/Core/Engine/GameEngine.cs:467-473 |
| 配置重载入口 ReloadConfigs（重载 catalog 后按 group 选择性重建运行时） | src/Core/Engine/GameEngine.cs:587-600 |
| game.json 特例：不走 catalog，恒为深合并后反序列化为 GameConfig | src/Core/Config/ConfigPipeline.cs:27-51 |
| graphs.json 合并后按 id 忽略大小写排序，保证注册顺序确定 | src/Core/NodeLibraries/GASGraph/Host/GraphProgramConfigLoader.cs:57 |
| LSW 保存服务：四类 GAS 文件固定写回 `assets/GAS/*.json` 常量路径，无布局探测 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEditModSaveService.cs:254-288 |

**相关文档**：[cfg-05 prd](../prd/cfg-05-config-pipeline.md) · [cfg-05 spec](../spec-runtime/cfg-05-config-pipeline.md) · [总篇](../README.md)
