# 开发规范深度材料

`gitbook/contributing/` 已是 Ludots 的正式开发规范入口。本目录保留仓库内的深度版规范、实现背景和配套说明，供本地阅读、审计和追溯使用。

## 目录

0.  [编码标准](00_coding_standards.md)
    *   核心架构铁律、ECS 约束、命名、Commit 格式、测试规范
1.  [Feature 开发工作流](01_feature_development_workflow.md)
    *   发现阶段、设计阶段、实现挂靠与验证清单
2.  [AI 辅助开发规范](02_ai_assisted_development.md)
    *   防幻觉、防重复造轮子、任务执行决策规范
3.  [开发环境与构建](03_environment_setup.md)
    *   SDK 要求、构建、测试和 launcher 入口
4.  [文档治理配套说明](04_documentation_governance.md)
    *   GitBook 与 `docs/` 的关系、校验与入口同步要求
5.  [共享 Skill 治理](05_shared_skill_governance.md)
    *   共享 skill 的仓库源、契约、校验与同步

## 与其他文档的关系

*   **正式开发规范**在 `gitbook/contributing/`——用于 GitBook 发布、入口导航和规范判断
*   **架构文档**在 `docs/architecture/`——描述引擎各子系统的深度设计与实现
*   **参考资料**在 `docs/reference/`——收纳查表型和操作型深度文档
*   **本文件夹**——为正式规范提供仓库内配套版和延伸说明
