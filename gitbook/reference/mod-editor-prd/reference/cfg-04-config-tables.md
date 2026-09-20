# cfg-04 reference · 配置目录

> 现状参考。第一性需求见 [cfg-04 PRD](../prd/cfg-04-config-tables.md)；配置说明见 [cfg-04 配置说明](../config/cfg-04-config-tables.md)；目标实现见 [cfg-04 runtime spec](../spec-runtime/cfg-04-config-tables.md)。

## 1. 现状快照

- 目录条目为六字段封闭 schema：Path / Policy / IdField（默认 id）/ ArrayAppendFields / ShardDirectories / AllowEmpty；白名单外属性抛错，Policy 五值 Ordinal 精确匹配，ArrayAppendFields 元素必须非空字符串。
- 目录文件自身按 Path 同 id 跨源合并，mod 可追加条目。
- 加载器对目录为单向查询：RequireEntry 查不到即抛错；目录侧无"条目是否被消费"的校验。
- 引擎默认目录现存条目数以事实页为准，位于 assets/ 根；TagDisplay 查表专线已按 ADR 移除（查表统一走通用用户表）；五张表启用分片目录（effects / abilities / graphs / preset_types / presenters，分片目录与主文件同根）。
- 分片能力已在 main（合并提交 9e05ca07f5）：目录条目六字段（新增 ShardDirectories / AllowEmpty）；五张表启用分片目录——GAS/effects、GAS/abilities、GAS/graphs、GAS/preset_types、Presentation/presenters，其中 abilities 与 presenters 同时声明 AllowEmpty（整表可空）。配套 showcase 与设计文档见架构章 mod-extensible-runtime-showcases/config-shards.md。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 目录加载（自身按 Path 同 id 合并、元素必须对象、六字段白名单、策略解析、IdField 默认 id、ArrayAppendFields 非空校验） | src/Core/Config/ConfigCatalogLoader.cs:9-98 |
| 加载器侧 fail-closed 查询（未登记路径抛错） | src/Core/Config/ConfigPipeline.cs:161-169 |
| 目录加载调用点（初始化与重载两处） | src/Core/Engine/GameEngine.cs:473、588 |
| 目录正本 | assets/config_catalog.json（assets/ 根） |
| 分片字段定义（ShardDirectories / AllowEmpty） | src/Core/Config/ConfigCatalogEntry.cs |
| 分片收集（主文件先、分片目录稳定枚举后） | src/Core/Config/ConfigPipeline.cs（LoadShardDirectory） |

**相关文档**：[cfg-04 prd](../prd/cfg-04-config-tables.md) · [cfg-04 spec](../spec-runtime/cfg-04-config-tables.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
