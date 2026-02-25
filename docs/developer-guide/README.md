# 开发者指南 - Developer Guide

本指南旨在帮助开发者快速了解 Ludots 框架的核心架构、开发原则和工具使用。

## 目录

1.  [ECS 开发实践与 SoA 原则](01_ecs_soa_principles.md)
    *   Arch ECS 使用规范
    *   组件结构与设计
    *   系统分组与执行顺序
2.  [Mod 架构与配置系统](02_mod_architecture.md)
    *   Mod 加载与依赖顺序
    *   虚拟文件系统
    *   配置加载与合并总览
3.  [适配器原则与平台抽象](03_adapter_pattern.md)
    *   Core 与平台解耦边界
    *   输入与渲染输出抽象
    *   Raylib 实现导航
4.  [CLI 启动与调试指南](04_cli_guide.md)
    *   脚本入口与工作目录约定
    *   ModLauncher CLI 常用命令
5.  [Pacemaker 时间与步进](05_pacemaker.md)
    *   固定步长与插值
    *   BudgetFuse 与 CooperativeSimulation
6.  [表现管线与 Performer 体系](06_presentation_performer.md)
    *   表现系统分层与数据流
    *   ResponseChain 与 UI 同步
7.  [ConfigPipeline 合并管线](07_config_pipeline.md)
    *   配置来源与优先级
    *   合并规则与冲突约束
8.  [Trigger 开发指南](08_trigger_guide.md)
    *   TriggerManager 与事件
    *   ScriptContext 与 ContextKeys
9.  [启动顺序与入口点](09_startup_entrypoints.md)
    *   Raylib App 与 Host 入口
    *   app/game.json 与 ModPaths
10. [Map、Mod 与空间服务可插拔](10_map_mod_spatial.md)
    *   MapConfig 合并与继承
    *   空间服务热切换与 SSOT
11. [GAS 分层架构与 Sink 最佳实践](11_gas_layered_architecture.md)
    *   系统组与 Phase 的分层 SSOT
    *   AttributeSink 与 Physics2D Sink 示例
12. [数据配置类与通用合并策略最佳实践](12_config_data_merge_best_practices.md)
    *   单例对象、键控表、多维配置的统一 merge 规则
    *   Mod 自定义配置类接入 ConfigPipeline 的标准流程
