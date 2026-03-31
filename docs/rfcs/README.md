# RFC 提案

本目录用于存放尚未纳入正式规范的提案。RFC 只能作为讨论材料，不能作为实现依据或规范来源。

## 1 目录

*   [RFC-0001 统一 Launcher CLI 与 Workspace 方案](RFC-0001-unified-launcher-cli-and-workspace.md)
    *   统一 Web Launcher、CLI 与 backend 的启动体验；引入显式 binding、递归扫描、适配层选择、`config/preset/preferences` 分层，以及可选 `game.json` bootstrap
*   [RFC-0002 Presentation Hotpath 可玩 Mod 设计](RFC-0002-presentation-hotpath-playable-mods.md)
    *   把 `#51` 的 shared technical harness 回写成三个玩家可感知的 playable mod 设计：crowd battlefield、town/base、hero focus/replay
*   [RFC-0052 表现层 snapshot playable mod 设计](RFC-0052-presentation-snapshot-playable-mods.md)
    *   为 visual snapshot contract 设计 3 个可被产品用户直接观察的 playable mod 场景，覆盖 skinned、static 与 hybrid lane
*   [RFC-0053 正式游戏可复用实体信息面板（UI + Overlay 双前端）](RFC-0053-entity-info-panels-for-ui-and-overlay.md)
    *   提议一套 UI + overlay 双前端共用的实体信息面板能力，覆盖 handle、target、采样面与 playable mod 表达
*   [RFC-0054 通用实体指令面板基础设施与演示 Mod 设计](RFC-0054-entity-command-panel-infra.md)
    *   提议一个多实例实体指令面板宿主，支持 trigger 驱动开关、按 slot 显示、技能组切换与 SoA/零分配热路径
*   [RFC-0055 UI surface ownership 与 showcase takeover 契约](RFC-0055-ui-surface-ownership-and-showcase-takeover.md)
    *   提议 retained UI、overlay 与 HUD 的 surface owner / lease / restore 契约，避免 mod 之间通过临时 suppression 相互踩踏
*   [RFC-0056 面向自动化设计与自动化开发的游戏文档体系](RFC-0056-game-design-harness-document-system.md)
    *   提议以 PREFAB 作为目录骨架、以 LTS 作为分工与投入元数据，并把 GDD / PRD / TDD / RLD / ADD / NDD 及 AI / 测试设计收敛为同一套可机读设计协议
*   [RFC-0057 英雄技能 Sandbox、全局施法模式与技能面板呈现](RFC-0057-champion-skill-sandbox-cast-mode-and-panel-presentation.md)
    *   提议以现有 selection / input / GAS / command panel / indicator 为基础，交付 EZ / 盖伦 / 杰斯技能 sandbox，并补齐全局施法模式与技能图标呈现基建
*   [RFC-0058 运行时具现体与空间查询策略统一合同](RFC-0058-runtime-manifestation-and-spatial-query-strategy-unification.md)
    *   提议把 projectile、summon、beam、zone、wall、trap 等运行时法术形态统一收敛到同一套 runtime manifestation + spatial query strategy 合同
*   [RFC-0059 路网移动 Order、Nav Runtime 与多策略路径演示统一方案](RFC-0059-road-order-nav-runtime-unification.md)
    *   提议把玩家 move order、nav runtime path、move sink 和 timeout/arrival 分层，同时用一个 unified showcase 演示 road graph、navmesh、cost area 与多 agent 策略

## 2 使用规则

*   RFC 被接受后，必须把正式结论回写到 `docs/conventions/`、`docs/architecture/` 或 `docs/reference/`。
*   RFC 被拒绝或过期后，应关闭并保留决策结果，不继续被正文引用。

## 3 相关文档

*   文档总览：见 [../README.md](../README.md)
*   架构决策记录：见 [../adr/README.md](../adr/README.md)
