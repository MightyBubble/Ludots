# Documentation Governance Report

Date: 2026-08-30
Scope: `gitbook/SUMMARY.md` and `gitbook/reference/narrative-runtime-wiki/`
Ruleset: 共享 `ludots-doc-governance` skill 的 `SKILL.md`、`references/doc-governance-checklist.md`、`references/link-validation.md`；仓库 `gitbook/contributing/documentation-governance.md`

## Summary

- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Validation

- `scripts/validate-docs.ps1 -Paths @('gitbook/reference/narrative-runtime-wiki','gitbook/SUMMARY.md')`：通过。
- `git diff --check`：通过。
- 新增正式页已接入 `gitbook/SUMMARY.md`。
- 新增页中的仓库路径引用均能解析到现有文件。
- Activity v5、Task v5 和 Record 设计页均包含“概述、结构、详情、场景、边界、UAT”六个章节。
- UAT 使用 Cucumber/Gherkin 的 `Feature`、`Scenario`、`Given`、`When`、`Then` 结构。

## Findings

无。

## Fix Order

本轮没有需要修复的治理问题。后续实现工作按以下顺序推进：

1. 先把 Activity v5 的 Graph 入口、作用域和超时合同落到运行时与测试。
2. 再实现 RecordLog、Record Graph 节点、存档和 topic producer。
3. 最后把 `task.md` 中标记为旧字段的 Provider 路径迁移到 Graph，并更新本报告和正式文档状态。

## Residual Risks

- Activity 当前实现仍使用 Provider 路径和旧字段；代码依据见 `src/Core/Gameplay/Activities/ActivityDefinitions.cs`、`src/Core/Gameplay/Activities/ActivityRuntimeService.cs`。
- Task 当前仍读取 `completion_rule`、`signal_key` 和 Provider 条件；v5 迁移前不能删除现有字段或存档格式。
- RecordLog、`WriteRecord`、`QueryRecordCount`、`QueryRecordExists` 和 Record 面板当前尚未实现；对应页面明确标记为设计稿。
