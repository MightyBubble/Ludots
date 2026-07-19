# docs/ —— 门户站点源码与深度材料

`docs/` 承担两个角色：GitHub Pages 门户站点的源码所在，以及仓库内的深度材料库（规范、架构、参考、决策、证据、提案）。

对外唯一门面是门户站点 **<https://mightybubble.github.io/Ludots/>**：CI 流水线把 `docs/`（站点源码 + 深度材料）、`gitbook/`（写作源 markdown）、`artifacts/acceptance/`（验收证据）与仓库根 `showcase.registry.json`（showcase 注册表）组装成 `_site/` 发布。仓库内的 markdown 不再是独立对外入口，新人一律以门户为准。

## 1 分层结构

| 层 | 目录 | 角色 |
|----|------|------|
| L1 正式规范 | `docs/conventions/` | 正式规范的仓库深度版（编码标准、配置与治理约定） |
| L2 架构 | `docs/architecture/` | 深度架构设计、模块边界、数据流与实现细节 |
| L3 参考 | `docs/reference/` | 长版操作手册、查表和补充材料 |
| L4 记录型 | `docs/adr/`、`docs/audits/`、`docs/rfcs/` | 架构决策记录（为什么这样定）、审计/验收/回顾证据、提案与讨论稿 |
| L5 站点生成区 | `index.html`、`diagrams.html`、`issues.html`、`assets/`、`prd/`、`tdd/`、`diagrams/` | 门户站点源码（手写 HTML/CSS/JS）与架构图库，经 `build-site.py` 组装进 `_site/` |

## 2 单源与入口

*   **写作源**：`gitbook/`（markdown，导航见 `gitbook/SUMMARY.md`）。正式规则、正式架构和正式操作文档统一在写作源维护。
*   **门户（唯一对外门面）**：<https://mightybubble.github.io/Ludots/> —— 文档、Showcase 画廊、测试验收证据与架构图库在此聚合发布。
*   **showcase 注册表**：仓库根 `showcase.registry.json`，登记每个 showcase 的层级（T1–T4）、文档、验收测试与证据目录。
*   **验收证据**：`artifacts/acceptance/`，由 CI 随门户一同发布。
*   `docs/` 中的深度材料用于补充实现细节、决策、证据和提案，不反向成为对外入口。

## 3 使用规则

*   正式规则、正式架构和正式操作文档统一定义在 `gitbook/` 写作源，并经门户对外发布。
*   当行为变化影响正式说明时，必须同步更新 `gitbook/`，并按需要回写受影响的深度材料。
*   代码行为变更时，同一提交或同一 PR 必须同步更新对应文档。
*   站点生成区（L5）的页面与图库面向门户发布，仓库内引用深度材料时走 L1–L4 的 markdown 索引。

## 4 相关文档

*   门户站点：<https://mightybubble.github.io/Ludots/>
*   写作源首页：见 [../gitbook/README.md](../gitbook/README.md)
*   写作源导航：见 [../gitbook/SUMMARY.md](../gitbook/SUMMARY.md)
*   文档治理规范：见 [../gitbook/contributing/documentation-governance.md](../gitbook/contributing/documentation-governance.md)
*   仓库 conventions 深度材料：见 [conventions/README.md](conventions/README.md)
*   仓库架构总索引：见 [architecture/README.md](architecture/README.md)
*   仓库参考资料总索引：见 [reference/README.md](reference/README.md)
