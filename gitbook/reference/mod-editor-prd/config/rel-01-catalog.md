# rel-01 配置说明 · 关系目录

> 配置写法与行为。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；编辑器需求见 [UXD](../uxd/rel-01-catalog.md)；现状见 [reference](../reference/rel-01-catalog.md)。

## 1. 示例配置

引擎默认 `assets/Relationships/catalog.json` 全量：

```json
{
  "types": [
    { "id": "Owns", "isSymmetric": false },
    { "id": "Controls", "isSymmetric": false },
    { "id": "MemberOf", "isSymmetric": false }
  ]
}
```

mod 增量示例（`mods/LudotsCoreMod/assets/Relationships/catalog.json`，同路径合并）：

```json
{
  "types": [
    { "id": "Hostile", "isSymmetric": false },
    { "id": "Friendly", "isSymmetric": false }
  ]
}
```

知识授予不写在 `catalog.json`，写在同目录的 `Relationships/projection.json`（`mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod`）：

```json
{
  "knowledgeGrants": [
    {
      "id": "participant-view.ally-share",
      "typeId": "ParticipantViews.KnowledgeShare",
      "collectionKey": "participant_view.ally.share",
      "presence": "HiddenWithSource",
      "position": "LastKnown",
      "attributes": ["Health"],
      "relationshipTypes": ["ParticipantViews.KnowledgeShare"],
      "confidencePermille": 760
    }
  ]
}
```

敌我没有专门的配置块。"A 队把 B 队当敌人"就是 A 队代表实体到 B 队代表实体之间有一条 `Hostile` 边；这条边写在 A 队代表实体的 `Relations` 上（`{ "To": "<B 队代表>", "Type": "Hostile" }`），反方向要在 B 队代表上再写一条；想让"友好"包含自己队伍，就在 A 队代表上写一条 `To` 指向自己的 `Friendly`。`Hostile`、`Friendly` 只是 LudotsCoreMod 声明的两个普通关系类型，Core 代码不认这两个名字；技能、AI、命令、表现的筛选里直接写类型名。

目录里只声明有人读的类型。两个队伍之间"有关系就行"的占位边没有任何功能读取，不要声明，也不要在地图里画。

## 2. 字段与行为

| 文件 | 块 | 字段与缺省 | 这样配会产生什么效果 |
|---|---|---|---|
| catalog.json | types | id、isSymmetric、template.components（可选） | 关系类型；非对称即有向边；写了 template 时边物化为实体并带上这些组件 |
| catalog.json | metrics | id、minValue=-100、maxValue=100、defaultValue=0 | 边上的数值 |
| catalog.json | flags | id | 边上的开关 |
| projection.json | knowledgeGrants | id、typeId、collectionKey、presence、position、attributes、relationshipTypes、tags、observedTick、expiryTick、confidencePermille=1000 | 沿 typeId 这种边把对方公开的实体集合按给定可见度分享给观察者 |

## 3. 文件结构

`Relationships/catalog.json` 和 `Relationships/projection.json` 都按 DeepObject 合并；引擎默认在 `assets/Relationships/`，mod 在各自 `assets/Relationships/` 下增量。`catalog.json` 只放 types / metrics / flags，`projection.json` 只放 knowledgeGrants。

## 4. 运行时加载效果

管线按 mod 装载顺序收集两个文件的片段，各块按 id 合并：第一次出现定顺序，后到的整条覆盖。加载完成后关系系统装配，图节点的关系符号（relationshipType / metric / flag，gr-02 字段族）和技能、AI、命令、表现里写的关系类型名都在这里解析。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 出现未知块或未知字段（比如已退役的 `reasons`、`bands`） | 装载失败，指明片段 |
| `catalog.json` 里写了 knowledgeGrants，或 `projection.json` 里写了 types / metrics / flags | 装载失败，指明片段 |
| 条目为 null、缺 id 或 id 为空白 | 装载失败，指明片段、块和下标 |
| confidencePermille 不在 1..1000 | 装载失败，指明条目 |
| 引用目录外的关系类型名（地图边、筛选、图节点） | 装载失败，指明引用位置 |

## 6. 实例

- 引擎默认：`assets/Relationships/catalog.json`
- mod 增量：`mods/LudotsCoreMod/assets/Relationships/catalog.json`（Hostile / Friendly）、`mods/showcases/capability_standard/CapabilityStandardParticipantViewsMod/assets/Relationships/`（情报共享类型与知识授予）
- 目录登记与启用分片计数见 [事实与取值表](../facts.md)

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [gr-02 配置说明](gr-02-document.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
