# ConfigPipeline 合并管线

本篇聚焦“配置从哪里来、如何合并、如何让 Mod 覆盖 Core”。在 Ludots 中，启动时的 `game.json` 只用于提供 ModPaths（引导信息）；所有实际运行配置由 ConfigPipeline 从 Core 与各 Mod 的配置片段合并得到。

## 1 配置来源与路径约定

ConfigPipeline 会从以下来源加载同一个 `relativePath` 的配置片段：

1.  Core 默认配置
    *   `Core:Configs/<relativePath>`
2.  Mod 配置（按 ModLoader 已解析的加载顺序遍历 `LoadedModIds`）
    *   `<modId>:assets/<relativePath>`
    *   `<modId>:assets/Configs/<relativePath>`

示例：当 `relativePath` 为 `game.json` 时，来源依次为：

*   `Core:Configs/game.json`
*   `<modId>:assets/game.json`
*   `<modId>:assets/Configs/game.json`

代码入口：

*   路径生成：`src/Core/Config/ConfigSourcePaths.cs`
*   统一加载：`src/Core/Config/ConfigPipeline.cs`

## 2 两条合并路径

ConfigPipeline 在不同场景下有两条合并路径：

### 2.1 MergeGameConfig

`MergeGameConfig()` 用于合并 `game.json`：

*   对象：递归合并
*   数组与标量：后者覆盖前者（不追加）

这是“简单且可预期”的合并规则，适合全局开关与参数表。

### 2.2 MergeFromCatalog

对更复杂的配置（尤其是“数组按 id 合并、可删除、可按字段追加”），推荐走配置目录驱动的合并：

*   `MergeFromCatalog(entry)` 或 `MergeFromCatalog(entry, report)`
*   合并策略由 `ConfigCatalogEntry` 指定（Replace、DeepObject、ArrayReplace、ArrayAppend、ArrayById）
*   ArrayById 支持 `Disabled` 或 `__delete` 删除条目，支持按字段追加数组

代码入口：

*   合并器：`src/Core/Config/ConfigMerger.cs`
*   合并策略枚举：`src/Core/Config/ConfigMergePolicy.cs`

## 3 冲突与可观测性

当同一配置片段在多个 Mod 中存在时，建议使用带 report 的合并路径，以便调试与追踪“最终胜出者来源”：

*   `MergeFromCatalog(entry, report)` 会记录 fragment 来源与 winner/删除信息
*   `GameEngine.InitializeWithConfigPipeline` 会把 `ConfigConflictReport` 存入 GlobalContext，供调试查看

## 4 Mod 覆盖的推荐写法

1.  将配置放在 Mod 的 `assets/Configs/` 下，与 Core 的相同相对路径对齐。
2.  对于数组型配置：
    *   如果合并规则是覆盖：在 Mod 里写完整数组，明确替换。
    *   如果你需要“按 id 改一点点”：使用 ConfigCatalog + ArrayById 策略的配置文件。
3.  避免依赖“隐式追加”的幻想：`MergeGameConfig` 的数组不追加，只覆盖。

## 5 配置类统一策略（必读）

如果你在新增一个“数据配置类”（尤其是给 Mod 暴露扩展点），不要再写独立合并器。  
请按统一策略设计并接入目录驱动合并，详见：

*   [数据配置类与通用合并策略最佳实践](12_config_data_merge_best_practices.md)

