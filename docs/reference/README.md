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
*   [Reactive UI runtime window 契约](reactive_ui_runtime_window_contract.md)
    *   `ReactivePage` virtual window 的当前 caller-owned 刷新契约、观测点与已验证路径。
*   [Arch ECS 外部依赖入口](arch_ecs_libraries.md)
    *   外部 Arch / Arch.Extended 源码入口与职责说明。

## 2 相关文档

*   文档总览：见 [../README.md](../README.md)
*   架构文档：见 [../architecture/README.md](../architecture/README.md)
*   文档治理规范：见 [../conventions/04_documentation_governance.md](../conventions/04_documentation_governance.md)
