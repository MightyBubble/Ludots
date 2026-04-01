# 仓库深度材料总览

`gitbook/` 是 Ludots 的唯一正式文档真相。`docs/` 不再承担正式入口职责，而是保存仓库内的深度设计说明、ADR、审计、RFC 和补充型实现材料。

## 1 分层结构

| 目录 | 角色 | 是否 SSOT |
|------|------|-----------|
| `docs/conventions/` | GitBook 正式规范的仓库配套版与深度说明 | 否 |
| `docs/architecture/` | 深度架构设计、模块边界、数据流与实现细节 | 否 |
| `docs/reference/` | 长版操作手册、查表和补充材料 | 否 |
| `docs/adr/` | 架构决策记录（为什么这样定） | 否，记录决策 |
| `docs/audits/` | 审计、验收、收束与回顾证据 | 否，记录证据 |
| `docs/rfcs/` | 提案与讨论稿 | 否，记录候选方案 |

## 2 阅读入口

*   [GitBook 首页](../gitbook/README.md) —— 当前正式入口与导航。
*   [贡献与开发](../gitbook/contributing/README.md) —— 当前正式开发规范。
*   [架构](../gitbook/architecture/README.md) —— 当前正式架构总览。
*   [参考资料](../gitbook/reference/README.md) —— 当前正式操作资料与查表信息。
*   [仓库 conventions 深度材料](conventions/README.md) —— 对应 GitBook 规范的仓库配套版。
*   [仓库架构材料](architecture/README.md) —— Core、Runtime、Gameplay、Presentation 等长篇设计说明。
*   [仓库参考资料](reference/README.md) —— CLI、标准规范与查表型深度文档。
*   [架构决策](adr/README.md) —— 关键决策与收敛原因。
*   [审计记录](audits/README.md) —— 审计、验收、收束矩阵和阶段性报告。
*   [RFC 提案](rfcs/README.md) —— 尚未纳入正式规范的提案。

## 3 使用规则

*   正式规则、正式架构和正式操作文档统一定义在 `gitbook/`。
*   `docs/` 中的内容用于补充实现细节、决策、证据和提案，不反向成为正式规范来源。
*   当行为变化影响正式说明时，必须同步更新 `gitbook/`，并按需要回写受影响的深度材料。
*   代码行为变更时，同一提交或同一 PR 必须同步更新对应文档。

## 4 相关文档

*   GitBook 文档首页：见 [../gitbook/README.md](../gitbook/README.md)
*   GitBook 文档治理：见 [../gitbook/contributing/documentation-governance.md](../gitbook/contributing/documentation-governance.md)
*   仓库 conventions 深度材料：见 [conventions/README.md](conventions/README.md)
*   仓库架构总索引：见 [architecture/README.md](architecture/README.md)
*   仓库参考资料总索引：见 [reference/README.md](reference/README.md)
