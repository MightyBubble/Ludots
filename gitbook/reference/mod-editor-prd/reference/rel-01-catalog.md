# rel-01 reference · 关系目录

> 现状参考。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；配置说明见 [rel-01 配置说明](../config/rel-01-catalog.md)。

## 1. 现状快照

- 装载现状：管线 loader 读 `Relationships/catalog.json`（types / metrics / flags）和 `Relationships/projection.json`（knowledgeGrants），都是 DeepObject；各块按 id 覆盖合并，首现定序、后到覆盖整条目。
- 字段现状：Type{Id,IsSymmetric,Template.Components}；Metric{Id,MinValue=-100,MaxValue=100,DefaultValue}；Flag{Id}；KnowledgeGrant{Id,TypeId,CollectionKey,Presence,Position,Attributes/AttributeIds,RelationshipTypes/RelationshipTypeIds,Tags/TagIds,ObservedTick,ExpiryTick,ConfidencePermille=1000}。
- 资产现状：引擎默认 catalog 3 个非对称 type（Owns / Controls / MemberOf）；LudotsCoreMod 增量声明 Hostile、Friendly；ParticipantViews 演示声明 `ParticipantViews.KnowledgeShare` 和两条知识授予。
- 反序列化现状：大小写不敏感、枚举字符串转换、未知字段拒绝；空条目、缺 id、块放错文件、置信度越界都装载失败。
- 遗留：`RelationshipBandRegistry`、`RelationshipCallbackProcessor`、`RelationshipSynergyProcessor` 等运行时结构仍在代码里，但对应配置块已经退役，数据永远是空的。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| loader 与合并 | src/Core/Gameplay/Relationships/Config/RelationshipCatalogPipelineLoader.cs |
| 四块结构定义 | src/Core/Gameplay/Relationships/Config/RelationshipCatalogConfig.cs |
| 知识授予编译 | src/Core/Gameplay/Relationships/RelationshipCatalogRuntime.cs |
| 引擎默认资产 | assets/Relationships/catalog.json |
| mod 增量资产 | mods/LudotsCoreMod/assets/Relationships/catalog.json 等 |
| 目录守卫 | src/Tests/ArchitectureTests/Governance/ArchitectureGuardTests.cs（RelationshipCatalogs_DeclareOnlyConsumedVocabulary） |

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [gr-02 reference](gr-02-document.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
