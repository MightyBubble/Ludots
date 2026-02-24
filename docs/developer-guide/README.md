# 开发者指南 (Developer Guide)

本指南旨在帮助开发者快速了解 Ludots 框架的核心架构、开发原则和工具使用。

## 目录

1.  [ECS 开发实践与 SOA 原则](01_ecs_soa_principles.md)
    *   Arch ECS 使用规范
    *   组件 (Component) 结构与设计
    *   系统 (System) 分层与执行顺序
2.  [Mod 架构与配置系统](02_mod_architecture.md)
    *   Mod 加载流程与依赖管理
    *   虚拟文件系统 (VFS)
    *   配置合并 (ConfigPipeline)
3.  [适配器模式与平台抽象](03_adapter_pattern.md)
    *   核心 (Core) 与平台 (Platform) 解耦
    *   输入与渲染抽象
    *   Raylib 适配器实现示例
4.  [CLI 启动与调试指南](04_cli_guide.md)
    *   命令行参数详解
    *   启动脚本使用
    *   调试配置
5.  [Pacemaker, Presenter 与 ConfigPipeline](05_pacemaker_presenter_config.md)
    *   Pacemaker 核心机制
    *   Presenter 响应链与视觉同步
    *   ConfigPipeline 深度合并与覆盖规则
