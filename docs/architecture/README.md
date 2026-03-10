# 架构文档

本目录描述 Ludots 当前实现的核心架构、模块边界和数据流。这里记录“系统现在如何工作”，不承担规范制定和审计留档职责。

## 1 Core 与 Runtime

*   [ECS 开发实践与 SoA 原则](ecs_soa.md)
*   [Mod 架构与配置系统](mod_architecture.md)
*   [适配器模式与平台抽象](adapter_pattern.md)
*   [Pacemaker 时间与步进](pacemaker.md)
*   [ConfigPipeline 合并管线](config_pipeline.md)
*   [Trigger 开发指南](trigger_guide.md)
*   [启动顺序与入口点](startup_entrypoints.md)
*   [Map、Mod 与空间服务可插拔](map_mod_spatial.md)
*   [Mod 运行时唯一真相与收束准则](mod_runtime_single_source_of_truth.md)
*   [统一 UI Runtime 与三前端写法](ui_runtime_architecture.md)

## 2 Gameplay 与 Presentation

*   [GAS 分层架构与 Sink 最佳实践](gas_layered_architecture.md)
*   [GAS 战斗体系基建与 MOBA 实践指南](gas_combat_infrastructure.md)
*   [表现管线与 Performer 体系](presentation_performer.md)
*   [3C 系统：相机、角色与控制](camera_character_control.md)

## 3 相关查表与证据

*   [CLI 运行与调试手册](../reference/cli_runbook.md)
*   [配置数据合并最佳实践](../reference/config_data_merge_best_practices.md)
*   [相机标准规范](../reference/camera_standards.md)
*   [3C 系统能力清单](../reference/3c_capability_matrix.md)
*   [最近提交审计与端到端交互验收](../audits/recent_commit_audit_and_e2e_showcase.md)
*   [版本收束处置矩阵](../audits/convergence_disposition_matrix.md)

## 4 相关文档

*   文档总览：见 [../README.md](../README.md)
*   开发规范：见 [../conventions/README.md](../conventions/README.md)
*   参考资料：见 [../reference/README.md](../reference/README.md)
