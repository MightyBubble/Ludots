# Doc Governance Report — #1567 切 0 文档合同

- 范围：`gitbook/architecture/spatial-scale-and-resolution-ssot.md`、`gitbook/reference/map-scale-authoring-guide.md`、`gitbook/reference/spatial-scale-configuration.md`
- 规则集：`skills` 共享 doc-governance（仓库相对路径、单一 SSOT、证据落路径、术语稳定）
- 目的：落地 [#1567 空间配置四域归位](https://github.com/MightyBubble/Ludots/issues/1567) 切 0——只改文档合同，不改代码与资产

## 变更摘要

| 文件 | 变更 |
|---|---|
| `gitbook/architecture/spatial-scale-and-resolution-ssot.md` | 头部回链 #1567；目标节增四域模型表；层级表更新 PartitionChunk/NavTile footprint/MacroTile/StreamingChunk/WorldExtent 行并新增 NavTileGranularity/BoardExtent/BoardOrigin 行；命名 Taxonomy 增 3 词；映射表追加 #1567 六行；配置指南表与配置到行为联动表补目标态行；DoD 回链 #1567 |
| `gitbook/reference/map-scale-authoring-guide.md` | 全页重写为四域 authoring 视角（世界尺寸直写、板=业务区域+摆放、nav 颗粒度独立、执行层不变）；顶部加目标态状态横幅；文末新增「迁移对照（#1567）」；保留全部原有外链与 MassNavigation/pathing 片段 |
| `gitbook/reference/spatial-scale-configuration.md` | 导语标注主表=现状；文末新增「四域归位目标键位（#1567）」查表 |

## 发现与处置

| 级别 | 发现 | 处置 |
|---|---|---|
| P3 | 三份文档中的 `mods/.../MassNavigationConfig.json`、`Navigation/navmesh.json`、`Navigation/agent_profiles.json`、`Navigation/pathing.json` 为 mod 资产根相对简写，仓库根下不解析 | 沿用原文档既有简写约定，非本次新增断链；未改 |
| — | 本次新增的全部 markdown 相对链接与锚点（SSOT 互链、board-addressing.md、迁移对照锚点）自检通过 | 无待办 |

## 链接自检

脚本：解析三份文档的 markdown 链接与反引号路径，对仓库根解析（排除 http 与通配 `...`）。结果：新增链接全部解析；上报项均属上表 P3 既有简写。

## SSOT 一致性

- 未新建平行 SSOT：四域词汇、owner、键位全部落在既有 `spatial-scale-and-resolution-ssot.md` 层级表/映射表；速查页与 authoring guide 明确声明以架构页为准。
- 术语稳定：`MacroTile`/`WorldExtent`/`PartitionChunk`/`TerrainChunk` 语义未改，只追加 #1567 目标态归属；新增 `BoardExtent`/`BoardOrigin`/`NavTileGranularity` 已进 Taxonomy。
- 现状/目标分离：三页均显式标注「#1567 切 N 落地前仍是现状键」，不存在宣称未实现行为的段落。
