# Architecture

`gitbook/architecture/README.md` is now the formal architecture entry for Ludots. This directory remains the repository-local deep-dive companion and documents behavior that exists in the repository today and can be backed by code, tests, or shipped runtime artifacts.

## Core Runtime

- [Adapter Pattern](adapter_pattern.md)
- [Camera Character Control](camera_character_control.md)
- [Config Pipeline](config_pipeline.md)
- [ECS and SoA](ecs_soa.md)
- [Entity Collection Query Infrastructure](entity_collection_query_infrastructure.md)
- [Entity Command Panel Infrastructure](entity_command_panel_infrastructure.md)
- [Entity Insight Panel Architecture](entity_insight_panel_architecture.md)
- [Exchange Architecture](exchange_architecture.md)
- [Item Inventory Equipment Architecture](item_inventory_equipment_architecture.md)
- [Map, Mod, and Spatial Ownership](map_mod_spatial.md)
- [Mod Architecture](mod_architecture.md)
- [Mod Runtime Single Source of Truth](mod_runtime_single_source_of_truth.md)
- [Launcher SSOT and User-First Endgame](launcher_ssot_user_first.md)
- [Pacemaker](pacemaker.md)
- [Runtime Entity Spawn Flow](runtime_entity_spawn_flow.md)
- [Spatial Geometry SSOT](spatial_geometry_ssot.md)
- [Capability Standard Showcases](../../gitbook/architecture/capability-standard-showcases.md)
- [Startup Entrypoints](startup_entrypoints.md)
- [Time Flow](time_flow.md)
- [Trigger Guide](trigger_guide.md)
- [UI Runtime Architecture](ui_runtime_architecture.md)
- [Browser UI Runtime](browser_ui_runtime.md)
- [WebUI DataPlane Architecture](webui_dataplane_architecture.md)
- [WebUI Panel Kit Manifest (WPK-1)](webui_panel_kit_manifest.md)
- [WebUI Resource Attribute Panel (WPK-2)](webui_resource_attribute_panel.md)
- [CommandDeck Multi-Display Modes (WPK-3)](command_deck_display_modes.md)
- [WebUI Production / Worker / Queue Overview (WPK-4)](webui_production_overview_panel.md)
- [WebUI Tooltip + Rich Text (WPK-5)](webui_tooltip_rich_text.md)
- [WebUI Quest Objective Panel (WPK-6)](webui_quest_objective_panel.md)
- [WebUI Notification Panel (WPK-7)](webui_notification_panel.md)
- [WebUI TechTree / Progression Panel (WPK-9)](webui_techtree_progression_panel.md)
- [WebUI Panel Kit Showcase Family (WPK-10)](webui_panel_kit_showcase_family.md)
- [WebUI Panel Kit Showcase Family UAT (WPK-10)](webui_panel_kit_showcase_family_uat.md)

## Target State And Migration

- [Launcher SSOT and User-First Endgame](launcher_ssot_user_first.md)
- [Mod Runtime Single Source of Truth](mod_runtime_single_source_of_truth.md)

## Gameplay and Presentation

- [GAS Combat Infrastructure](gas_combat_infrastructure.md)
- [GAS Layered Architecture](gas_layered_architecture.md)
- [AI Utility Autocast Contract](../../gitbook/architecture/ai-utility-autocast-contract.md)
- [Order / Navigation / Movement Architecture](order_navigation_movement.md)
- [Quest Core Infrastructure](quest_core_infra.md)
- [Story Runtime：Dialogue / Sequencer](story_runtime_dialogue_sequencer.md)（#1083 SSOT）
- [（已废止）Narrative Dialogue / Cinematic](narrative_dialogue_cinematic.md)
- [（已废止）Narrative Frontend Kit](narrative_frontend_kit.md)
- [Interaction Architecture](interaction/README.md)
- [Persistent Static Adapter Sync](persistent_static_adapter_sync.md)
- [Presentation Presenter](presentation_presenter.md)
- [Presentation Snapshot Contract](presentation_snapshot_contract.md)
- [Entity Selection Architecture](entity_selection_architecture.md)

## Related References

- [CLI Runbook](../reference/cli_runbook.md)
- [Config Data Merge Best Practices](../reference/config_data_merge_best_practices.md)
- [Camera Standards](../reference/camera_standards.md)
- [3C Capability Matrix](../reference/3c_capability_matrix.md)
- [Recent Commit Audit and E2E Showcase](../audits/recent_commit_audit_and_e2e_showcase.md)
- [Convergence Disposition Matrix](../audits/convergence_disposition_matrix.md)

## Repository Docs

- [GitBook Architecture](../../gitbook/architecture/README.md)
- [Docs Overview](../README.md)
- [Conventions](../conventions/README.md)
- [Reference Docs](../reference/README.md)
