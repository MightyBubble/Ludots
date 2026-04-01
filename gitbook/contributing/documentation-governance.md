# 文档治理

本页定义 Ludots 文档的正式治理规则。

## 1 正式真相

`gitbook/` 是唯一正式文档真相。

规则如下：

- 正式规则写在 `gitbook/contributing/`
- 正式架构写在 `gitbook/architecture/`
- 正式操作手册与查表写在 `gitbook/reference/`
- `gitbook/README.md` 和 `gitbook/SUMMARY.md` 是发布导航入口

## 2 仓库其他文档的角色

- `docs/architecture/`：深度设计说明、实现细节和迁移中的技术材料
- `docs/reference/`：更长的操作材料、查表与补充证据
- `docs/adr/`：决策记录
- `docs/audits/`：审计、验收和收束证据
- `docs/rfcs/`：提案

这些目录不再是正式入口。它们可以承载深度材料，但不得与 `gitbook/` 产生相互矛盾的正式规则。

## 3 单点归属

一个正式事实只能在 `gitbook/` 里定义一次。

其他仓库文档可以：

- 链接正式页面
- 补充实现证据
- 补充历史决策和审计结果

其他仓库文档不得：

- 重新定义正式规则
- 保留“旧版仍可用”之类的兼容描述
- 用审计结论或 RFC 代替正式规范

## 4 入口与导航

- 新增、删除或重命名正式文档时，必须同步更新 `gitbook/SUMMARY.md`
- 若影响仓库入口，还必须同步更新 `README.md`、`README_CN.md`、`AGENTS.md`、`CLAUDE.md`
- 若影响治理或校验，还必须同步更新 `.github/workflows/docs-governance.yml`、`.github/CODEOWNERS`、`scripts/validate-docs.ps1`

## 5 语言与证据

- 正式正文使用中文，技术名词保留英文原文
- 结论优先附源码路径、测试路径或深度材料路径
- 代码行为变更时，同一提交或同一 PR 必须同步更新正式文档

## 6 深度材料

- 仓库配套版：`docs/conventions/04_documentation_governance.md`
