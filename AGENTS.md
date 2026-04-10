# AGENTS.md

Ludots — 基于 Arch ECS 的高性能 C# 游戏框架。六边形架构，一切皆 Mod，禁止 fallback/向后兼容/重复造轮子/跨越职责。

**写任何代码前必须先读 `gitbook/contributing/ai-assisted-development.md` 的“任务执行决策规范”。**

所有正式开发规范统一维护在 `gitbook/`：

| 文档 | 路径 |
|------|------|
| 文档首页 | `gitbook/README.md` |
| 规范总索引 | `gitbook/contributing/README.md` |
| 编码标准（含核心铁律） | `gitbook/contributing/coding-standards.md` |
| Feature 开发工作流 | `gitbook/contributing/feature-development-workflow.md` |
| **AI 辅助开发规范（必读）** | `gitbook/contributing/ai-assisted-development.md` |
| 开发环境与构建 | `gitbook/contributing/environment-setup.md` |
| 文档治理规范 | `gitbook/contributing/documentation-governance.md` |
| 共享 Skill 治理 | `gitbook/contributing/shared-skill-governance.md` |
| 架构文档索引 | `gitbook/architecture/README.md` |
| 共享 Skill 索引 | `skills/README.md` |
| 共享 Skill 注册表 | `skills/registry.json` |

## 最近踩坑

- 新建独立 playground/mod 时，先确认该 mod 自己的 `assets/game.json` 已显式打开所需核心运行时开关；这次“右键命令成功但单位完全不动”的低级根因，就是新 mod 漏了 `Navigation2D.Enabled = true`，导致命令链在跑、仿真链没注册。
