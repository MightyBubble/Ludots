# Scenario: entity-command-panel-showcase

## Header
- build: GasTests / EntityCommandPanelShowcase_SwitchesM6AggregationProfilesAtRuntime
- seed: map-authored deterministic scenario
- map: interaction_showcase_hub
- clock: engine fixed step sampled through 1/60s test ticks
- execution timestamp UTC: 2026-08-18T07:09:09.7547967+00:00
- host mod: EntityCommandPanelShowcaseMod
- launcher binding: `entity_command_panel_showcase`
- manual GUI command: `.\scripts\run-mod-launcher.cmd cli launch entity_command_panel_showcase --adapter raylib`
- panel source: gas.collection-ability-slots / CollectionGasEntityCommandPanelSource
- visible panel: WebUI/CEF bottom command panel; old Compose command panel stays closed.

## Scenario Card
- Player goal: switch the command panel between Family, Template, and Ability aggregation profiles and verify the live M6 collection regroups immediately.
- Gameplay domain: RFC-0065 SHOW-4 / P3 runtime aggregation preference over the M6 command-source collection.
- Initial entities: local player command owner plus Arcweaver, Vanguard, and Commander showcase command providers.
- Action script: load `interaction_showcase_hub`, verify toolbar/source registries, activate Family, Template, and Ability buttons, then copy slots from the registered collection source.
- Primary success condition: by-family collapses the three heroes into eight command families, by-template shows 24 unit-template slots, and by-ability shows 21 distinct ability definitions.
- Failure branch condition: missing source registry entry, missing by-family profile fragment, toolbar not bound to source, stale revision, or copied slots bypassing aggregation.

## Runtime Evidence
- collection key: `collection.command.source`; rows: 3; title: M6 Aggregation Profiles
- by-family fragment: `mods/EntityCommandPanelMod/assets/UI/ability_aggregation_profiles.json` declares `aggregation.by_family` groupBy `catalog.castFamily` overflow `nextPanelSlot`.
- installed profile registry ids: aggregation.by_template=1, aggregation.by_family=3, aggregation.by_ability_id=2

## Timeline
- t+000: verified launcher binding `entity_command_panel_showcase` -> `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod` and loaded `interaction_showcase_hub`.
- t+004: showcase host published `collection.command.source` for Arcweaver, Vanguard, and Commander.
- t+005: toolbar provider exposed Template, Family, and Ability profile buttons with Family active.
- t+006: WebUI DataPlane snapshot reported ready=true, sourceActorCount=3, and active profile tile counts.
- t+profile: activated Family (`aggregation.by_family`), revision 1784404673, copied 8 slots.
- t+profile: activated Template (`aggregation.by_template`), revision 1458872427, copied 24 slots.
- t+profile: activated Ability (`aggregation.by_ability_id`), revision 2266766512, copied 21 slots.

## Outcome
- result: success
- headless evidence: registry/source lookup, by-family config fragment, host-mod command collection, toolbar activation, revision changes, and aggregation slot counts all passed.
- visible evidence boundary: this artifact set is produced by the filtered GasTests acceptance run; it does not claim a captured raylib/CEF video.

## Artifacts
- `artifacts/acceptance/entity-command-panel-showcase/aggregation-profile-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/battle-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/trace.jsonl`
- `artifacts/acceptance/entity-command-panel-showcase/path.mmd`
