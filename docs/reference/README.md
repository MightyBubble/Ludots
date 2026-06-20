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
* [OpenRA AI 底层系统源码分析](openra_ai_system_analysis.md)
  * OpenRA skirmish AI 的 Player trait 入口、BotModule 组合、Order 边界、分队状态机与 Ludots 可借鉴点
* [OpenRA AI 分层架构剖面](openra_ai_layered_architecture.html)
  * 以可视化 HTML 拆解 OpenRA AI 从大厅配置、Player 激活、BotModule 调度到 Order 执行的完整分层
* [OpenRA AI 与 Ludots 架构对比分析](openra_ludots_ai_comparison.html)
  * 对比 OpenRA RTS bot 模块群与 Ludots 现有 AI / Mod / ConfigPipeline / Order 基建，列出优势、缺口与建议路线
* [OpenRA 用户输入动作与基础单位指令源码剖面](openra_unit_behavior_attack_move_analysis.html)
  * 从玩家输入动作、屏幕反馈、命令生成、trait 解析和 Activity 树角度拆解 Move / Attack / Stop / Guard / Stance / Deploy / Harvest / Repair / Capture / Transport / Minefield 等基础指令
* [OpenRA 索敌优先级、脱战警戒与炮台机制源码剖面](openra_targeting_priority_guard_turret_analysis.html)
  * 聚焦 AutoTarget / AutoTargetPriority / AttackFollow / Guard / Turreted / Armament，拆解索敌优先级、脱战、警戒范围和炮台 gameplay 约束
* [Ludots 索敌、警戒、炮台与 Order/GAS 映射缺口分析](ludots_targeting_order_gas_gap_analysis.html)
  * 对照 OpenRA 单位自动战斗链路，审视 Ludots 当前 Order / GAS / Navigation / Team / TargetResolver 基建，列出缺口与映射路线
* [Ludots 通用 Utility AI SoA 架构方案](ludots_utility_ai_soa_openra_behavior_architecture.html)
  * 对照 Uintel Utility Intelligence ECS/GO 文档和 Ludots 现有 AI / Order / GAS / Graph 基建，提出 SoA、0Alloc、配置驱动 OpenRA 基础行为的通用 AI 系统方案
* [Ludots AI 现状、困境与参考地图](ludots_ai_status_reference_map.html)
  * 汇总当前 AI 相关现状文档、困境、取舍方向和参考资料，作为后续设计和实现的入口总图
* [Ludots 通用 Utility AI 与 Autocast 仲裁 SSOT 方案](ludots_ai_utility_autocast_ssot_plan.html)
  * 以"普攻=autocast 能力、多能力仲裁=Utility 决策入口"为核心命题，完整复刻 Uintel 架构并把 OpenRA stance/基础 order 作为业务语义行为包；给出三层模型、SoA/0Alloc 运行时、分层 SSOT 边界、SystemGroup 排布、Epic + 10 个 sub-issue 拆解与验收标准

## 2 相关文档

* 文档总览：见 [../README.md](../README.md)
* 架构文档：见 [../architecture/README.md](../architecture/README.md)
* 文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
