# RFC 提案

本目录用于存放尚未纳入正式规范的提案。RFC 只能作为讨论材料，不能作为实现依据或规范来源。

## 1 目录

* [RFC-0001 统一 Launcher CLI 与 Workspace 方案](RFC-0001-unified-launcher-cli-and-workspace.md)
  * 统一 Web launcher、CLI 与 backend 的启动体验；引入显式 binding、递归扫描、适配层选择，以及 `config/preset/preferences` 分层
* [RFC-0002 Presentation Hotpath 可玩 Mod 设计](RFC-0002-presentation-hotpath-playable-mods.md)
  * 把 `#51` 的 shared technical harness 回写成三个玩家可感知的 playable mod 设计
* [RFC-0052 表现层 Snapshot Playable Mod 设计](RFC-0052-presentation-snapshot-playable-mods.md)
  * 为 visual snapshot contract 设计三个可被产品用户直接观察的 playable mod 场景
* [RFC-0053 正式游戏可复用实体信息面板（UI + Overlay 双前端）](RFC-0053-entity-info-panels-for-ui-and-overlay.md)
  * 提议一套 UI + overlay 双前端共用的实体信息面板能力
* [RFC-0054 通用实体指令面板基础设施与演示 Mod 设计](RFC-0054-entity-command-panel-infra.md)
  * 提议一个多实例实体指令面板宿主，支持 trigger 驱动开关、按 slot 显示与技能组切换
* [RFC-0055 UI Surface Ownership 与 Showcase Takeover 契约](RFC-0055-ui-surface-ownership-and-showcase-takeover.md)
  * 提议 retained UI、overlay 与 HUD 的 surface owner / lease / restore 契约
* [RFC-0057 英雄技能 Sandbox、全局施法模式与技能面板呈现](RFC-0057-champion-skill-sandbox-cast-mode-and-panel-presentation.md)
  * 提议以现有 selection / input / GAS / command panel / indicator 为基础交付 champion skill sandbox
* [RFC-0058 运行时具现体与空间查询策略统一合同](RFC-0058-runtime-manifestation-and-spatial-query-strategy-unification.md)
  * 提议把 projectile、summon、beam、zone、wall、trap 等运行时法术形态统一到一套 runtime 合同
* [RFC-0059 Entity-Relation Selection Container SSOT](RFC-0059-entity-selection-container-ssot.md)
  * 选择真相改为 selection container 与 relation membership，并已回写到架构文档
* [RFC-0059 路网移动 Order、Nav Runtime 与多策略路径演示统一方案](RFC-0059-road-order-nav-runtime-unification.md)
  * 提议把玩家 move order、nav runtime path、move sink 和 timeout/arrival 分层，并用 unified showcase 演示
* [RFC-0060 AI Utility Autocast 契约收敛](RFC-0060-ai-utility-autocast-contract.md)
  * 定稿 Intent / Behavior / Deliberation 三层、普攻=autocast、AI Order/GAS 边界与加载期 fail-fast 契约
* [RFC-0060 通用存档系统](RFC-0060-universal-save-system.md)
  * Epic #292 引用的通用存档 RFC 回写；编号与 AI Utility Autocast 历史文件重叠，正式结论以 `gitbook/architecture/save-system.md` 为准
* [RFC-0065 统一交互—集合—施法架构](RFC-0065-unified-interaction-collection-casting-architecture.md)
  * 整合 RFC-0061~0064 为单一 Epic SSOT：Context Stack、Control Plane、Provenance Presenter，并补齐 Panel Router / 聚合 catalog / CastFlow / CastDispatch；含 Mod 开发者与玩家双 persona 的 BDD UAT 验收
  * Epic issue 粘贴稿：[RFC-0065-epic-issue-body-draft.md](RFC-0065-epic-issue-body-draft.md)（issue 建立后本稿即可删除）
* [RFC-0066 Agent 调试桥](RFC-0066-agent-debug-bridge.md)
  * 已合入 main（PR #1001）。这个号已经被占用。旧 PR `#907` 曾误用同一编号谈 GAS 定容，那条枝作废。
* [RFC-0067 GAS 装载期定容世界级存储](RFC-0067-gas-loadtime-capacity-world-store.md)
  * 提案：属性/标签种类在装载期按本局内容定容，对局内仍定长、零分配；用世界级 SoA 列存替换实体内嵌 64/256 硬顶
  * Epic 粘贴稿：[RFC-0067-epic-issue-body-draft.md](RFC-0067-epic-issue-body-draft.md)（issue 建立后本稿即可删除）
  * 关单/建单粘贴稿：[issue-hygiene-2026-08-25.md](issue-hygiene-2026-08-25.md)

## 2 使用规则

* RFC 被接受后，必须把正式结论回写到 `gitbook/contributing/`、`gitbook/architecture/` 或 `gitbook/reference/`
* RFC 被拒绝或过期后，应关闭并保留决策结果，不继续被正文引用

## 3 相关文档

* 文档总览：见 [../README.md](../README.md)
* 架构决策记录：见 [../adr/README.md](../adr/README.md)
