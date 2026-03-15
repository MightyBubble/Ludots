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

## 2 使用规则

*   RFC 被接受后，必须把正式结论回写到 `docs/conventions/`、`docs/architecture/` 或 `docs/reference/`。
*   RFC 被拒绝或过期后，应关闭并保留决策结果，不继续被正文引用。

## 3 相关文档

*   文档总览：见 [../README.md](../README.md)
*   架构决策记录：见 [../adr/README.md](../adr/README.md)
