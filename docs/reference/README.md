# 参考资料

本目录收录仓库内的事实性、查表型、操作型深度文档。正式对外入口已切换到 `gitbook/reference/`，本目录继续承担实现细节、长版手册和补充证据角色。

## 1 目录

* [CLI 运行与调试手册](cli_runbook.md)
  * Mod launcher 工作目录、参数、脚本入口、launcher graph artifact 与 direct-debug 边界
* [配置数据合并最佳实践](config_data_merge_best_practices.md)
  * ConfigPipeline 扩展点、配置类设计与合并规则
* [相机标准规范](camera_standards.md)
  * Editor / Runtime 相机对齐约定和配置标准
* [3C 系统能力清单](3c_capability_matrix.md)
  * 3C 系统现状、能力边界和接入点总览
* [Arch ECS 外部依赖入口](arch_ecs_libraries.md)
  * 外部 `Arch` / `Arch.Extended` 源码入口与职责说明
* [Champion Skill Stress Scenario](champion_skill_stress_scenario.md)
  * `ChampionSkillSandboxMod` 双阵营压力地图的场景卡、复用清单、工具面板与验收要求
* [Champion Skill Sandbox Delivery Plan](champion_skill_sandbox_delivery_plan.md)
  * `ChampionSkillSandboxMod` 的复用基线、交付内容与当前实现切片回写
* [关系系统：市场案例抽象与 Ludots 复用设计](relationship_system_market_abstraction.md)
  * CRPG / JRPG / 自走棋 / 三国英雄题材的关系机制抽象、Ludots 基建复用清单、配置与 showcase 验收口径

## 2 相关文档

* 文档总览：见 [../README.md](../README.md)
* 架构文档：见 [../architecture/README.md](../architecture/README.md)
* 文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
