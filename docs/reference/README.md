# 参考资料

本目录收纳事实性、查表型、操作型文档。这里描述“当前有哪些契约、命令和标准”，不负责解释完整设计取舍。

## 1 目录

*   [CLI 运行与调试手册](cli_runbook.md)
    *   ModLauncher 工作目录、参数和调试入口。
*   [配置数据合并最佳实践](config_data_merge_best_practices.md)
    *   ConfigPipeline 扩展点、配置类设计与合并规则。
*   [相机标准规范](camera_standards.md)
    *   Editor / Runtime 相机对齐约定和配置标准。
*   [3C 系统能力清单](3c_capability_matrix.md)
    *   3C 系统现状、能力边界和接入点总览。
*   [Arch ECS 外部依赖入口](arch_ecs_libraries.md)
    *   外部 Arch / Arch.Extended 源码入口与职责说明。
*   [共享 Skill 工作流总览](shared_skill_workflow_overview.md)
    *   shared skill 参与文档、设计、交付、测试、证据、handoff、PR / CI 的总流程与分流程。
*   [工具链实际工作流总览](toolchain_workflow_overview.md)
    *   当前仓库内产品入口、开发工具、治理脚本与 CI 的实际工作流地图。
*   [KOEI 历史策略说明书与拆解来源总表](koei_historical_strategy_manual_sources.md)
    *   `三國志11/12/13` 与 `信長の野望・革新/天道/創造/新生` 的说明书、在线手册和拆解边界。
*   [官方手册转述 Digest](manual_digests/README.md)
    *   当前包含《三國志11》与《信長之野望・革新》官方节选手册的中文转述拆解工程。
*   [游戏设计 Harness 案例包](game_design_harness_examples/README.md)
    *   `Tetris`、`Red Alert` 风格 RTS、口袋怪兽式怪兽收集 RPG，以及 `三國志/信長` 谱系拆解的完整项目级设计案例包。
*   [Vibe Kanban 研发操作手册](vibekanban_delivery_playbook.md)
    *   看板状态、issue 拆解、workspace dispatch、变更/重构/事故/skill 操作 playbook。
*   [Champion Skill Stress Scenario](champion_skill_stress_scenario.md)
    *   `ChampionSkillSandboxMod` 双阵营压力地图的场景卡、复用清单、工具面板与验收要求。
*   [关系系统：市场案例抽象与 Ludots 复用设计](relationship_system_market_abstraction.md)
    *   CRPG/JRPG/自走棋/三国英雄题材的关系机制抽象、Ludots 基建复用清单、配置/回调点与 showcase 验收口径。

## 2 相关文档

*   文档总览：见 [../README.md](../README.md)
*   架构文档：见 [../architecture/README.md](../architecture/README.md)
*   文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
