# 文档治理配套说明

正式文档治理规则已经切换到 `gitbook/contributing/documentation-governance.md`。本文件只保留仓库内的配套说明，帮助维护 `docs/` 与 `gitbook/` 的关系。

## 1 正式入口

- 正式文档真相：`gitbook/`
- GitBook 入口：`gitbook/README.md`
- GitBook 导航：`gitbook/SUMMARY.md`

## 2 docs 的当前角色

`docs/` 负责保存：

- 深度实现说明
- 架构决策记录
- 审计与验收证据
- RFC 与尚未落地的提案
- GitBook 页面引用的仓库补充材料

`docs/` 不再承担正式规范的单点归属。

## 3 仓库维护规则

- 修改正式规则、正式架构或正式操作手册时，先更新 `gitbook/`
- 若深度材料被 GitBook 页面引用，需在同一提交内同步修正 `docs/`
- 若新增、删除或重命名正式页面，必须同步更新 `gitbook/SUMMARY.md`
- 若影响入口或校验，还必须同步更新 `README.md`、`README_CN.md`、`AGENTS.md`、`CLAUDE.md`、`.github/CODEOWNERS`、`.github/workflows/docs-governance.yml`、`scripts/validate-docs.ps1`

## 4 路径规则

- Markdown 链接使用相对路径
- 源码和文档路径使用仓库相对路径，如 `src/...`、`gitbook/...`、`docs/...`
- 不使用绝对本地路径和 `file://` 链接

## 5 相关文档

- 正式文档治理：见 [../../gitbook/contributing/documentation-governance.md](../../gitbook/contributing/documentation-governance.md)
- GitBook 首页：见 [../../gitbook/README.md](../../gitbook/README.md)
- 仓库深度材料总览：见 [../README.md](../README.md)
