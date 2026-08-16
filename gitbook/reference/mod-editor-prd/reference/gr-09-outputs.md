# gr-08 reference · Query 图输出

> 现状参考。第一性需求见 [gr-08 PRD](../prd/gr-09-outputs.md)；配置说明见 [gr-08 配置说明](../config/gr-09-outputs.md)。

## 1. 现状快照

- outputs 声明现状：GraphOutputConfig 九字段（Id/Destination/Type/Source/Key/CollectionKey/Role/Title/Summary）；Destination 两值（Summary=0/EntityCollection=1）；ValueKind 五值（Bool/Int/Float/Entity/TargetList）；EntityCollection 须 TargetList+collectionKey 必填；Summary 禁 TargetList、source 须存在类型匹配、key 缺省 outputId。
- 回写器现状：强制 Query + RequireAllowed + 有 schema + owner/caster 非空；帧绑 TargetContext；EntityCollection 经描述符 Create+Replace(owner, descriptor, TargetList)；Summary 写 Bool/Int/Float/Entity 四类。
- 值存储现状：SOA 槽池 + 双哈希 + 世代 + 修订号 + 退休队列；容量来自 gasRuntimeCapacity.graphOutputValueCapacity；清理系统订阅实体销毁入队退休。
- 资产现状：主线 graphs 无 Query 图、无 outputs 声明；回写器注册为服务，Core 层无调用点。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| outputs 编译与 schema | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.cs:1905-2081 |
| 配置结构 | src/Core/NodeLibraries/GASGraph/GraphOutputTypes.cs:7-20 |
| 回写物化 | src/Core/NodeLibraries/GASGraph/GraphReturnWriter.cs:34-181 |
| 值存储槽池 | src/Core/NodeLibraries/GASGraph/GraphOutputValueStore.cs:24-128 |
| 容量接线 | src/Core/Engine/GameEngine.cs:927-929 |
| 实体销毁清理 | src/Core/Gameplay/GAS/Systems/GraphOutputValueCleanupSystem.cs:8-39 |

**相关文档**：[gr-08 PRD](../prd/gr-09-outputs.md) · [gr-01 reference](gr-02-document.md) · [gr-07 reference](gr-08-mount-points.md)
