# MassNavigation Selection Migration

## Overview: Player And Goal

Select a formation, preview its members, then send it around obstacles with a right click. This changes selection in the existing 10k showcase; it does not redesign navigation, HUD, or animation.

## Structure: Main Loop And Scenarios

Drag over friendly units, see the yellow preview, release to commit blue markers, and right-click a destination. Repeat on moving units. Case E and MassNavigation consume one asset-only selection module. Each scene binds its own candidate query to the same collection contract. The small Case E map is the minimal scenario; MassNavigation is the moving, grounded 10k scenario.

## Details: Controls And Explanation

| Runtime control | Values | Player question |
|---|---|---|
| Selection rectangle | Pointer start/end | Which formation will move? |
| Selection operation | Replace / Shift add / Alt subtract | Can I adjust the group? |
| Destination | Ground point | Can the group route around obstacles? |
| Camera | Pan / rotate / zoom | Do markers track moving units on relief? |

Preview versus committed selection is the comparison: while dragging, the command group stays unchanged; release changes it. Empty ground clears the group. Yellow preview, blue selection and the live rectangle are presenter-owned. Existing health and minimap surfaces remain owned by their current systems.

## Boundaries: API Audit And Reuse

- Reuse InputConfigPipelineLoader, InteractionContext profiles and TriggerGraph mounting.
- Reuse BindQueryCollection, QueryScreenRegionCollection, WriteCollection and the existing Case E graphs.
- Reuse EntityCollectionStore and presenter collection events; arrays and ECS systems retain their existing capacity contracts.
- Extract shared assets into SelectionInteractionMod without a C# entry point or startup map.
- Keep scene-specific candidate queries in their scene Mods. MassNavigation uses team 1 and Unit.Commandable.
- Disable CoreInput's legacy acquisition in the MassNavigation game config; selection is mounted by the shared interaction context and TriggerGraph assets.
- Navigation orders read selected. No new selection system or per-unit scan is added.
- The production 10k contract runs with the existing launcher entry. A separate bridge launch was attempted on port 47922; startup reached the `mass_navigation` map but the existing Raylib 512-bone cap rejected the 615-bone soldier asset before an interactive frame. This is an independent renderer debt, not a selection result.

## Scenarios: Launch And Evidence

Use the repository launcher with the existing MassNavigation launch graph after rebuilding its resolved plan. Use a separate Agent Bridge port, retaining the user's existing 47921 process. Record the loaded Mods/map, preview and selected membership, right-click orders, movement and visible markers. Headless frame timings are not whole-machine FPS.

## UAT

```gherkin
Feature: Select and command formations in the 10k field
  Scenario: Preview then move a friendly formation
    Given the 10k field is running with friendly and opposing formations
    When I drag across units without releasing
    Then the rectangle and yellow preview follow my pointer
    And my committed selection stays unchanged
    When I release the mouse
    Then only eligible units receive blue selection markers
    And the rectangle and yellow preview disappear
    When I right-click clear ground
    Then the selected units move toward that destination
    And their markers remain attached while I rotate the camera

  Scenario: Clear the group
    Given I have selected a formation
    When I select empty ground
    Then its blue markers disappear
    And a subsequent move command does not move the previous selection

  Scenario: Adjust the group
    Given I have selected a formation
    When I hold Shift and select another formation
    Then both formations remain selected
    When I hold Alt and select part of that group
    Then those units are removed from the selection
```

## GAS Composition Self-Review

- Task: Reuse Case E selection in MassNavigation. Date: 2026-09-09. Author: Codex.
- Core judgment: A, PASS. Behavior is existing graph composition and authored query parameters.
- Layers: Layer 2 contains input/context/query/presenter assets; existing Core operations and collection runtime supply Layer 0. No transaction executor is added.
- Handlers: no new handlers. Systems: existing TriggerGraph, interaction-context, collection-query and presenter systems. Registries: existing graph, tag, template and collection registries.
- New Layer 0 op: `QueryFilterControllable`, used by the MassNavigation box-hit wrapper to apply the same control-domain rule as Case E.
- Transaction boundary: retain existing collection-write and context-deactivation semantics and explicit capacity errors.
- Config SSOT: SelectionInteractionMod owns shared selection assets; scene Mods own candidate binding and movement mappings.
- Schema: one installation flag in existing CoreInput acquisition config, outside GAS composition. No new profile schema or loader.
- Red flags: no lifecycle enum, parallel spawn path, placement validation, or fallback added.
- Next variant: change query graph parameters and map mount, without changing Core enums.

Status: asset migration complete; targeted contracts and production path tests pass. Full Raylib interactive acceptance remains blocked by the pre-existing 512-bone renderer cap (`615 > 512`) before the first usable frame.
