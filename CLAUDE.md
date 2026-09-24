# CLAUDE.md

Ludots — 基于 Arch ECS 的高性能 C# 游戏框架。六边形架构，一切皆 Mod，禁止 fallback/向后兼容/重复造轮子/跨越职责。

**写任何代码前必须先读 `gitbook/contributing/ai-assisted-development.md` 的“任务执行决策规范”。**

Entity Association Core 的计划与 ADR SSOT 在 GitHub issue #239；ADR 正本在 #244，仓库 `docs/adr/` 不新增 AAC 平行 ADR 文件。

正式文档门户：<https://mightybubble.github.io/Ludots/>（文档 / Showcase 画廊 / 测试验收 / 架构图库一站聚合）；写作源：`gitbook/`（`gitbook/SUMMARY.md` 导航）；showcase 与验收注册表：仓库根 `showcase.registry.json`。

## Agent skills

### Issue tracker

Issues and PRDs are tracked in `MightyBubble/Ludots` GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the repository's existing GitHub label vocabulary and do not create labels implicitly. See `docs/agents/triage-labels.md`.

### Domain docs

Ludots is a single-context repository; use `gitbook/architecture/` and `docs/adr/` as the domain and decision sources. See `docs/agents/domain.md`.
