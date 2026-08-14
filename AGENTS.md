# AGENTS.md

Ludots — 基于 Arch ECS 的高性能 C# 游戏框架。六边形架构，一切皆 Mod，禁止 fallback/向后兼容/重复造轮子/跨越职责。

**写任何代码前必须先读 `gitbook/contributing/ai-assisted-development.md` 的“任务执行决策规范”。**

代码注释纪律：类型、方法、字段命名到位时不写注释——注释是命名无法表达"非显然的意图、取舍、约束"时的补救，不是默认动作；禁止在代码注释里写 issue/PR 编号、修复历史等项目管理痕迹（那些属于 commit message 与 issue，代码里只留合同本身）。

Entity Association Core 的计划与 ADR SSOT 在 GitHub issue #239；ADR 正本在 #244，仓库 `docs/adr/` 不新增 AAC 平行 ADR 文件。

正式文档门户：<https://mightybubble.github.io/Ludots/>（文档 / Showcase 画廊 / 测试验收 / 架构图库一站聚合）；写作源：`gitbook/`（`gitbook/SUMMARY.md` 导航）；showcase 与验收注册表：仓库根 `showcase.registry.json`。图能力的进度、还开着的活、不该合的 PR，只认 `gitbook/architecture/graph-capability-status.md`。不要另写交接。旧审计不是入口。
