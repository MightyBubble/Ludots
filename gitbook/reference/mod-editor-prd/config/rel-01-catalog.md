# rel-01 配置说明 · 关系目录

> 配置写法与行为。第一性需求见 [rel-01 PRD](../prd/rel-01-catalog.md)；编辑器需求见 [UXD](../uxd/rel-01-catalog.md)；现状见 [reference](../reference/rel-01-catalog.md)。

## 1. 示例配置

引擎默认 `assets/Relationships/catalog.json` 现状全量：

```json
{
  "types": [
    {
      "id": "Owns", "isSymmetric": false, "role": "Ownership",
      "rules": { "maxIncoming": 1, "onFull": "Replace", "acyclic": true }
    },
    { "id": "Controls", "isSymmetric": false, "role": "ControlGrant" },
    { "id": "MemberOf", "isSymmetric": false, "role": "Membership" }
  ],
  "metrics": [], "flags": [], "bands": [], "reasons": [], "callbacks": [], "synergies": [],
  "knowledgeGrants": [],
  "stance": {
    "stanceTypes": ["Hostile", "Friendly", "Neutral"],
    "sameDomainStance": "Friendly", "sameTeamStance": "Friendly", "defaultStance": "Neutral"
  }
}
```

mod 增量示例（`mods/LudotsCoreMod`，同文件合并）：`"types": [ { "id": "LudotsCore.Participant", "isSymmetric": false } ]`。

## 2. 字段与行为

| 块 | 字段与缺省 | 这样配会产生什么效果 |
|---|---|---|
| types | Id、IsSymmetric、Role（缺省 None）、Rules（缺省无） | 关系类型；非对称即有向边。Role 只告诉引擎回答控制面问题时读哪种关系：`Ownership`（顺着它往上找到玩家，算出 PlayerOwner）、`Membership`（顺着它找到队伍代表，算出 Team）、`ControlGrant`（控制域在拥有的之外再加上它指向的目标）。Role 本身不带任何连边限制；“一个实体只能有一个主人”这类规矩写在 Rules 里。引擎只认 Role 和 Rules，不认类型名，改名不用动代码 |
| types[].rules | MaxIncoming=0、MaxOutgoing=0（0 表示不限）、OnFull（设了上限就必须写 `Reject` 或 `Replace`）、Acyclic=false、BlockedAny=[]、Removed=[] | 这种关系每次连边都要守的规矩，不管是技能效果、图、地图加载还是 C# 调用连的。`MaxIncoming` / `MaxOutgoing`：一个终点最多被连几次 / 一个起点最多连出几条；满了按 `OnFull` 处理，`Reject` 报错并保留旧边，`Replace` 拆掉旧边再连新边（只能配上限 1）。`Acyclic`：会绕成环的边直接报错。`BlockedAny`：同一对实体之间已有这些关系时，这条连不上。`Removed`：连这条时，把同一对实体之间的这些关系拆掉（两种关系互斥就互相写进对方的 Removed）。被拒绝的连边不会改动任何已有的边 |
| metrics | Id、Min=-100、Max=100、Default=0 | 关系度量（数值画像） |
| flags | Id | 布尔旗标 |
| bands | Id、TypeId、MetricId、FlagId、Threshold(short)、Comparison（缺省 GreaterOrEqual） | 度量档位：过阈值授旗标 |
| reasons | Id | 变化原因（记录与回调引用） |
| callbacks | Id、TypeId、MetricId、Min/Max 可空、EventKey、ExitEventKey、八组 tag 列表 | 度量区间进出发事件 |
| synergies | Id、RequireAllTags、MinimumCount=1、ApplyTagsToTeam、EventKey | 组合协同 |
| knowledgeGrants | Id、TypeId、CollectionKey、Presence/Position、Attribute/Relationship/Tag 引用、ObservedTick、ExpiryTick、ConfidencePermille=1000 | 关系知识授予 |
| stance | StanceTypes、SameDomain/SameTeam/Default | 姿态词表与缺省——整对象替换 |

## 3. 文件结构

默认路径 `Relationships/catalog.json`（DeepObject 合并）；引擎默认在 `assets/Relationships/catalog.json`，mod 在各自 `assets/Relationships/` 下增量。

## 4. 运行时加载效果

管线按目录装载，九块分别按 id 覆盖合并（stance 整对象替换）；加载完成后关系系统装配，图节点的关系符号（relationshipType/metric/reason/flag，gr-02 字段族）引用此目录解析。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| JSON 反序列化失败（类型不符等） | 装载失败，指明片段 |
| 图节点引用目录外的关系 id | 图装载失败（gr-04 符号解析） |
| 条目缺 id 或 id 为空白 | 该条目跳过不合并 |
| 合并后 Ownership / Membership / ControlGrant 任一角色没有类型认领 | 引擎启动失败，报出缺的角色名 |
| 两个类型认领同一个角色 | 引擎启动失败，报出两个类型名 |
| 对称类型带了角色 | 引擎启动失败：角色要分清起点和终点 |
| mod 用同 id 覆盖默认类型却没写 role | 整条覆盖后角色丢失，按“缺角色”启动失败 |
| Ownership 角色的类型没有写 `rules.maxIncoming: 1` | 引擎启动失败：要往上找唯一的根主人，就必须规定一个实体只有一个直接上级 |
| rules 设了上限却没写 onFull，或写了 onFull 却没设上限 | 引擎启动失败，报出类型名 |
| `onFull: Replace` 配了大于 1 的上限 | 引擎启动失败：多条旧边时说不清该顶掉哪条 |
| 对称类型写了 maxIncoming / maxOutgoing / acyclic | 引擎启动失败：这几条规矩要分清起点和终点 |
| blockedAny / removed 写了目录里没有的类型、写了自己，或同一类型两边都写 | 引擎启动失败，报出类型名和字段 |
| 运行中连边违反 rules（满了且 Reject、会成环、被 blockedAny 挡住） | 这次连边报错，报出类型名和违反的是哪条规矩；已有的边不变 |

## 6. 实例

- 引擎默认：`assets/Relationships/catalog.json`；mod 增量：`mods/LudotsCoreMod/assets/Relationships/catalog.json`、`mods/CombatStanceBehaviorMod/assets/Relationships/catalog.json`
- 目录登记与启用分片计数见 [事实与取值表](../facts.md)

**相关文档**：[rel-01 PRD](../prd/rel-01-catalog.md) · [gr-02 配置说明](gr-02-document.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
