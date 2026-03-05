# 开发规范

本文件夹是 Ludots 仓库所有开发规范的唯一入口。所有 AI Agent 配置文件（`CLAUDE.md`、`AGENTS.md`）均重定向到此处。

## 目录

0.  [编码标准](00_coding_standards.md)
    *   **核心架构铁律**：六边形架构与无头测试、一切皆 Mod、四个禁止（fallback/向后兼容/重复造轮子/跨越职责）
    *   ECS 硬性约束、组件/系统命名、Commit 格式、测试规范
1.  [Feature 开发工作流](01_feature_development_workflow.md)
    *   发现阶段（已有能力检索清单）
    *   设计阶段（挂靠点、API 引用验证）
    *   实现阶段（架构挂靠原则）
    *   验证阶段（提交前检查清单）
2.  [AI 辅助开发规范](02_ai_assisted_development.md)
    *   防幻觉条款（搜索→阅读→编码→自检）
    *   禁止创建平行体系
    *   能力清单速查表（20+ Registry、核心管线、SystemGroup Phase）
3.  [开发环境与构建](03_environment_setup.md)
    *   SDK 要求与安装
    *   构建、测试、运行命令
    *   平台特定说明（Linux / Cloud VM）

## 与其他文档的关系

*   **架构文档**在 `docs/developer-guide/`——描述引擎各子系统的设计与实现
*   **本文件夹**——规定开发流程和编码标准（必须遵守的规则）
*   两者互补，不重叠
