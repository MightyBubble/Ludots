# rel-01 reference · 关系目录

> 现状参考。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；配置说明见 [rel-01 配置说明](../config/rel-01-catalog.md)。

## 1. 现状快照

- 装载现状：管线 loader 默认路径 `Relationships/catalog.json`、DeepObject；九块（types/metrics/flags/bands/reasons/callbacks/synergies/knowledgeGrants/stance）按 id 覆盖合并，首现定序、后到覆盖整条目、空 id 跳过；stance 整对象替换。
- 字段现状：Type{Id,IsSymmetric}；Metric{Id,MinValue=-100,MaxValue=100,DefaultValue}；Flag{Id}；Band{Id,TypeId,MetricId,FlagId,Threshold(short),Comparison 缺省 GreaterOrEqual}；Reason{Id}；Callback{Id,TypeId,MetricId,Min/Max(int?),EventKey,ExitEventKey,八组 tag 列表}；Synergy{Id,RequireAllTags,MinimumCount=1,ApplyTagsToTeam,EventKey}；KnowledgeGrant{Id,TypeId,CollectionKey,Presence,Position,AttributeIds,RelationshipTypeIds,TagIds,ObservedTick,ExpiryTick,ConfidencePermille=1000}；Stance{StanceTypes,SameDomainStance,SameTeamStance,DefaultStance}。
- 资产现状：引擎默认 catalog 3 个非对称 type（Owns/Controls/MemberOf），metrics/flags/bands/reasons/callbacks/synergies/knowledgeGrants 全空，stance 词表 Hostile/Friendly/Neutral、同域/同队 Friendly、缺省 Neutral；mod 侧另有增量（如 LudotsCore.Participant）。
- 反序列化现状：大小写不敏感、枚举字符串转换，无未知字段拒绝。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| loader 与合并 | src/Core/Gameplay/Relationships/Config/RelationshipCatalogPipelineLoader.cs:23-28,64-75 |
| 九块结构定义 | src/Core/Gameplay/Relationships/Config/RelationshipCatalogConfig.cs |
| 反序列化选项 | RelationshipCatalogPipelineLoader.cs:12-16 |
| 引擎默认资产 | assets/Relationships/catalog.json |
| mod 增量资产 | mods/LudotsCoreMod/assets/Relationships/catalog.json 等 |

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [gr-01 reference](gr-02-document.md) · [cfg-05 reference](cfg-05-config-pipeline.md)
