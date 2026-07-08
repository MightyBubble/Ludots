# 架构决策记录

本目录存放 Ludots 的架构决策记录。ADR 用于说明“为什么采用当前方案”，不重复完整规范与实现细节。

## 1 目录

*   [ADR-0001 文档 SSOT 分层结构](ADR-0001-docs-ssot-layout.md)
*   [ADR-0002 统一 UI Runtime 与三前端写法](ADR-0002-unified-ui-runtime-and-authoring-models.md)
*   [ADR-0003 Exchange Operation 与 Scope Key 身份模型](ADR-0003-exchange-operation-scope-key.md)
*   [ADR-0004 In-session Transport Network Editor Boundary](ADR-0004-transport-network-editor-boundary.md)
*   [ADR-0005 Live Map Editor Phase 2 Parity Boundary](ADR-0005-live-map-editor-phase2-parity-boundary.md)

## 2 编写规则

*   ADR 只记录决策背景、备选方案、结论和影响面。
*   决策一旦落地，正式规则应回写到 `docs/conventions/`，正式设计应回写到 `docs/architecture/` 或 `docs/reference/`。

## 3 相关文档

*   文档总览：见 [../README.md](../README.md)
*   文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
