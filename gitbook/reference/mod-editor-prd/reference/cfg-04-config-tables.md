# cfg-04 reference · 配置目录

> 现状参考。产品承诺见 [cfg-04 prd](../prd/cfg-04-config-tables.md)；目标实现见 [cfg-04 spec](../spec-runtime/cfg-04-config-tables.md)。

## 1. 现状快照

- 目录条目为四字段封闭 schema：Path / Policy / IdField（默认 id）/ ArrayAppendFields；白名单外属性抛错，Policy 五值 Ordinal 精确匹配，ArrayAppendFields 元素必须非空字符串。
- 目录文件自身按 Path 同 id 跨源合并，mod 可追加条目。
- 加载器对目录为单向查询：RequireEntry 查不到即抛错；目录侧无"条目是否被消费"的校验。
- 引擎默认目录现存 71 条，其中 GAS 相关 14 条，另覆盖 AI、输入、表现、导航、物品、叙事等配置类型。
- 分片能力已在 main（合并提交 9e05ca07f5）：目录条目六字段（新增 ShardDirectories / AllowEmpty）；五张表启用分片目录——GAS/effects、GAS/abilities、GAS/graphs、GAS/preset_types、Presentation/presenters，其中 abilities 与 presenters 同时声明 AllowEmpty（整表可空）。配套 showcase 与设计文档见架构章 mod-extensible-runtime-showcases/config-shards.md。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 目录加载（自身按 Path 同 id 合并、元素必须对象、四字段白名单、策略解析、IdField 默认 id、ArrayAppendFields 非空校验） | src/Core/Config/ConfigCatalogLoader.cs:9-98 |
| 加载器侧 fail-closed 查询（未登记路径抛错） | src/Core/Config/ConfigPipeline.cs:161-169 |
| 目录加载调用点（初始化与重载两处） | src/Core/Engine/GameEngine.cs:469、584 |
| 目录正本 | assets/Configs/config_catalog.json |
| 分片字段定义（ShardDirectories / AllowEmpty） | src/Core/Config/ConfigCatalogEntry.cs |
| 分片收集（主文件先、分片目录稳定枚举后） | src/Core/Config/ConfigPipeline.cs（LoadShardDirectory） |

**相关文档**：[cfg-04 prd](../prd/cfg-04-config-tables.md) · [cfg-04 spec](../spec-runtime/cfg-04-config-tables.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
