# Ludots 文档

`gitbook/` 是 Ludots 的正式文档源。GitBook 发布内容、仓库入口页、Agent 指引与 PR 审核均以这里为准。

Ludots 是一个基于 Arch ECS 的高性能 C# 游戏框架，核心约束如下：

- 六边形架构：`src/Core/` 不依赖平台层，gameplay 逻辑必须可无头测试。
- 一切皆 Mod：功能通过 Mod、Registry、Pipeline 和 SystemGroup 挂靠，不把业务硬编码进引擎。
- 四个禁止：禁止 fallback、禁止向后兼容、禁止重复造轮子、禁止跨越职责。

## 阅读路径

- 新成员先看 [快速开始](quick-start.md)
- 参与开发先看 [贡献与开发](contributing/README.md)
- 理解引擎设计先看 [架构](architecture/README.md)
- 图能力收口现在走到哪，只看 [图能力收口现状](architecture/graph-capability-status.md)
- 查命令、入口和目录时看 [参考资料](reference/README.md)

## 文档边界

- `gitbook/`：正式规则、正式架构说明、正式操作手册。
- `docs/`：仓库内深度材料、ADR、审计、RFC 与实现证据。
- `skills/`：共享 agent skill 源码与机器注册表。

当 `gitbook/` 与 `docs/` 出现冲突时，以 `gitbook/` 为准，并在同一提交内修正 `docs/`。
