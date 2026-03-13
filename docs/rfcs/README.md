# RFC 提案

本目录用于存放尚未纳入正式规范的提案。RFC 只能作为讨论材料，不能作为实现依据或规范来源。

## 1 目录

*   [RFC-0001 统一 Launcher CLI 与 Workspace 方案](RFC-0001-unified-launcher-cli-and-workspace.md)
    *   统一 Web Launcher、CLI 与 backend 的启动体验；引入显式 binding、递归扫描、适配层选择、`config/preset/preferences` 分层，以及可选 `game.json` bootstrap
*   [RFC-0002 Reactive UI Runtime Window Closure 与 Playable Mod 设计](RFC-0002-reactive-ui-runtime-window-closure-and-playable-mods.md)
    *   以 generic lifecycle integration 作为 reactive virtualization 的收口目标，并补齐 inventory、combat log、GM browser 三个 playable mod 设计包

## 2 使用规则

*   RFC 被接受后，必须把正式结论回写到 `docs/conventions/`、`docs/architecture/` 或 `docs/reference/`。
*   RFC 被拒绝或过期后，应关闭并保留决策结果，不继续被正文引用。

## 3 相关文档

*   文档总览：见 [../README.md](../README.md)
*   架构决策记录：见 [../adr/README.md](../adr/README.md)
