# RFC-0064 Collection Provenance & Performer — 多控制域 Marker 与观战投影

Status: Proposed  
Epic: [#538](https://github.com/MightyBubble/Ludots/issues/538)

## 1. 问题

框选 collection 混合多个 control domain（自有 unit + 代理队友 unit）时：

- 本地玩家需区分 marker 颜色（深绿 vs 浅绿）
- 直播裁判需同时看到所有玩家的 selection collection，并按队伍色 + 相位差异渲染（红 vs 橘）
- Performer 若读 `PlayerOwner` 组件或单一 Selection hub，无法表达 **provenance**

Collection row 目前仅有 `entity, ordinal, roleId, flags`，缺少 **control domain** 语义。

## 2. 结论

### 2.1 Provenance 模型

每条 collection row 必须可解析：

```text
row.entity          → 被选中的 embodied entity
row.controlDomain   → 哪个 player rep entity 拥有该行的「指挥域」
row.relationKind    → owns | controls (proxy)
```

两种实现路径（Epic 内择一，禁止双轨）：

| 方案 | 做法 | 优劣 |
|------|------|------|
| A. 显式 row metadata | 写入时填 `rowRoleId` / descriptor 扩展 | 热路径快，Performer 零图遍历 |
| B. 运行时反查 | Performer 调 `ControlDomainQuery.TryResolveControlDomain` | 无 duplicate，但热路径贵 |

**推荐 A**：CollectionWrite 在 filter 后写入时填充 provenance；ArchitectureTests 保证与 association 一致。

### 2.2 Performer 职责

Performer **只读** collection + provenance + catalog，**不写** collection、**不改** association。

```text
(localClient, collection.command.source) rows
  → for each row:
      style = PerformerCatalog.resolve(
        viewerRole: LocalPlayer,
        controlDomain: row.controlDomain,
        relationKind: row.relationKind,
        teamPalette: teamRep)
```

示例 catalog：

```json
{
  "id": "performer.selection.marker.local",
  "when": { "viewer": "localPlayer", "relationKind": "owns" },
  "asset": "selection_ring_deep_green"
},
{
  "id": "performer.selection.marker.proxy",
  "when": { "viewer": "localPlayer", "relationKind": "controls" },
  "asset": "selection_ring_light_green"
}
```

### 2.3 裁判 / 观战 multi-viewer

中立 player rep entity（refereeRep）通过 **Knowledge grant** 获得读其他 playerRep collection 的能力：

```text
refereeRep reads:
  (player1Rep, collection.command.source)
  (player2Rep, collection.command.source)
  ...
Performer(viewer=refereeRep):
  player1 domain → team_red_phase0
  player2 domain → team_red_phase1  // 同队色 + 相位差
```

禁止专用「RefereeSelectionService」— 复用 `EntityCollectionStore.TryGetView` + knowledge visibility。

### 2.4 事件

复用现有 Performer 事件：

- `EntityCollectionMemberAdded` / `Removed` + collection key
- revision tick 驱动 diff

Performer rule 条件增加：

- `collectionKey` match
- `controlDomain` match（from row metadata）
- `viewerRole` match（local / referee / spectator profile）

## 3. 与 Context Stack 的配合

不同 context 的 collection 可绑定不同 performer catalog：

| activeKey | Performer profile |
|-----------|-------------------|
| collection.command.source | selection.marker.* |
| collection.ability.nuke.targets | ability.nuke.target_marker |

Context pop 后 performer 订阅 revision 自动切换样式。

## 4. 分层边界

| 层 | 做 | 不做 |
|----|-----|------|
| CollectionWrite | 写 row + provenance | 选颜色 |
| ControlDomainQuery | 解析 domain | 渲染 |
| KnowledgeProjection | 裁判 visibility | 复制 collection |
| PerformerRuleSystem | 读 provenance → spawn marker | 写 selection |
| PresentationEventStream | 通知 revision | gameplay 语义 |

## 5. Sub-issues（PROV-*）

Parent: [#538](https://github.com/MightyBubble/Ludots/issues/538)

| ID | Issue |
|----|-------|
| PROV-1 | [#555](https://github.com/MightyBubble/Ludots/issues/555) provenance schema |
| PROV-2 | [#557](https://github.com/MightyBubble/Ludots/issues/557) CollectionWrite provenance |
| PROV-3 | [#559](https://github.com/MightyBubble/Ludots/issues/559) PerformerCatalog markers |
| PROV-4 | [#561](https://github.com/MightyBubble/Ludots/issues/561) Performer rules |
| PROV-5 | [#563](https://github.com/MightyBubble/Ludots/issues/563) Referee knowledge grant |
| PROV-6 | [#564](https://github.com/MightyBubble/Ludots/issues/564) Team palette + phase |
| PROV-7 | [#565](https://github.com/MightyBubble/Ludots/issues/565) Showcase |
| PROV-8 | [#566](https://github.com/MightyBubble/Ludots/issues/566) 文档 + tests |

## 6. 依赖

- RFC-0063 ControlDomainQuery（provenance 来源）
- RFC-0062 Context Stack（active key → performer profile）
- EntityCollectionStore revision 事件（已有）
- KnowledgeProjection（#190）

## 7. 非目标

- 不在 Performer 内实现 box cast
- 不新增 Selection 兼容 ring API
- 不为裁判写 gameplay order 路径

## 8. 验收

- [ ] 混合 owns + proxy controls 框选，本地深绿/浅绿 marker 正确
- [ ] 裁判同时见 player1/player2 collection，颜色相位正确
- [ ] Performer 零 `PlayerOwner` 读取
- [ ] Collection revision 驱动 marker diff，无全量 rebuild 抖动
- [ ] Playable showcase artifacts
