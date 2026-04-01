# 架构文档总览

`docs/architecture/` 是 Ludots 当前已落地架构的 SSOT 入口。这里的文档只描述仓库里已经存在、并且能被代码与测试路径佐证的运行时行为。

## 核心运行时

- [Adapter Pattern](adapter_pattern.md)
- [Camera Character Control](camera_character_control.md)
- [Config Pipeline](config_pipeline.md)
- [ECS and SoA](ecs_soa.md)
- [Entity Command Panel Infrastructure](entity_command_panel_infrastructure.md)
- [Map, Mod, and Spatial Ownership](map_mod_spatial.md)
- [Mod Architecture](mod_architecture.md)
- [Mod Runtime Single Source of Truth](mod_runtime_single_source_of_truth.md)
- [Pacemaker](pacemaker.md)
- [Runtime Entity Spawn Flow](runtime_entity_spawn_flow.md)
- [Startup Entrypoints](startup_entrypoints.md)
- [Time Flow](time_flow.md)
- [Trigger Guide](trigger_guide.md)
- [UI Runtime Architecture](ui_runtime_architecture.md)

## 玩法与表现

- [GAS Combat Infrastructure](gas_combat_infrastructure.md)
- [GAS Layered Architecture](gas_layered_architecture.md)
- [Order / Navigation / Movement Architecture](order_navigation_movement.md)
- [Interaction Architecture](interaction/README.md)
- [Persistent Static Adapter Sync](persistent_static_adapter_sync.md)
- [Presentation Performer](presentation_performer.md)
- [Presentation Snapshot Contract](presentation_snapshot_contract.md)
- [Entity Selection Architecture](entity_selection_architecture.md)

## 相关参考

- [CLI Runbook](../reference/cli_runbook.md)
- [Config Data Merge Best Practices](../reference/config_data_merge_best_practices.md)
- [Camera Standards](../reference/camera_standards.md)
- [3C Capability Matrix](../reference/3c_capability_matrix.md)
- [Recent Commit Audit and E2E Showcase](../audits/recent_commit_audit_and_e2e_showcase.md)
- [Convergence Disposition Matrix](../audits/convergence_disposition_matrix.md)

## 仓库文档入口

- [Docs Overview](../README.md)
- [Conventions](../conventions/README.md)
- [Reference Docs](../reference/README.md)
