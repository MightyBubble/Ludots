# RFC-0065 PR581 Workflow Closeout

> Current closeout note. Older audit sections in this file captured the pre-retirement
> transition state; they are superseded by this pass. Formal `SelectionRuntime`,
> `SelectionSetKeys`, `SelectionViewKeys`, `SelectionContextRuntime`, `SelectionControlGroupRuntime`,
> `OrderSelectionReference`, `SelectionRequest`, and `SelectionResponse` are retired; `EntityCollectionStore`
> / `collection.command.source` is the authoritative model.

Date: 2026-07-09

Scope: PR #581 follow-up review against `main`, latest GitHub PR reviews, PR head `2417820e9`, `docs/audits/rfc-0065-implementation-handoff.md`, and `docs/rfcs/RFC-0065-unified-interaction-collection-casting-architecture.md`.

## Executive Status

PR581 has reached the current RFC-0065 closeout target for formal Selection retirement and visible showcase evidence. Remaining RFC follow-ups are explicitly scoped below and are not fallback permission.

| Question | Status | Evidence |
|---|---|---|
| Are all follow-up TODOs done? | No | A1 full CEF toggle/revoke, A2 WebUI War3 command panel, SHOW-3 GUI marker/palette, A3 world-space superweapon timeline, A4 command-source/scheme, A4 world-space blink/mixed-dispatch timeline, B1 benchmarks, B2 current-workstation perf rerun, launcher bindings, and focused acceptance are complete. Terminal RFC work still includes Workflow C migrations, full video files where reviewers require video beyond timeline PNGs, and a dedicated isolated B2 perf host rerun if that stricter gate is required. |
| Are all UAT/showcases done? | Yes for the current framebuffer/timeline UAT pass | A1 has readable CEF toggle/revoke evidence; A2 now has readable WebUI/CEF War3 bottom-panel Template -> Family -> Ability evidence; SHOW-3 has readable GUI referee marker/palette evidence; A3 and A4 have player-readable world-space timelines with visible units, rings, and state changes. Full RFC §6 video recordings remain a separate artifact request if reviewers require video files instead of accepted timeline PNG evidence. |
| Is Selection retired? | Yes for formal Selection APIs and core command authority | Production and test code now use `EntityCollectionStore`, explicit owner/key collection reads, and `CommandSourceAcquisitionSystem`. `EntityCollectionContextRuntime` is intentionally collection-generic and does not hard-code or fall back to command-source. The deleted formal APIs must not be reintroduced. User-facing "selection" wording may remain only as shorthand for explicit entity collections. |

## Final 2026-07-09 Selection-Retirement Pass

This pass closes the remaining formal-selection cleanup for PR581:

- Core helpers that need a focused set of entities now accept an explicit `owner + collectionKey` or explicit actor span. They do not ask whether that set came from "selection", command-source acquisition, a showcase seed, or another business workflow.
- `EntityCollectionContextRuntime` remains a neutral collection reader. It resolves only the caller-provided collection key and does not register or imply a `collection.command.source` fallback.
- `MassNavigation` remains an execution domain. It consumes explicit move orders and does not read Selection, command-source, or interaction-context authority APIs.
- Current interaction architecture docs were migrated from old selection-oriented target-field, gate, and filter wording to `targetType` / `TargetCollectionGate` / `TargetFilter`, so new examples no longer teach the retired input model.
- `AbilityExecLoader` was tightened to fail fast for malformed ability config instead of silently skipping invalid effect ids, tags, timeline ticks, gate payloads, graph ids, caller params, toggle effects, or presentation mode overrides.

Latest validation from this pass:

```text
dotnet test src\Tests\GasTests\GasTests.csproj --no-build --filter "FullyQualifiedName~AbilityExecLoaderFailFastTests|FullyQualifiedName~ParticipantBindingContractTests|FullyQualifiedName~LifecycleArchitectureTests|FullyQualifiedName~TagEffectArchitectureTests|FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~ProgressionRequirementTests|FullyQualifiedName~RoadNetworkShowcaseTests|FullyQualifiedName~MudSc2AndYgoDemoTests|FullyQualifiedName~ArchitectureGuardTests"

Passed: 227/227
```

```text
dotnet test src\Tests\ThreeCTests\ThreeCTests.csproj --filter "FullyQualifiedName~Camera"

Passed: 77/77
```

```text
dotnet test src\Tests\PresentationTests\PresentationTests.csproj --filter "FullyQualifiedName~MassNavigation|FullyQualifiedName~FormationCapabilityShowcaseContractTests|FullyQualifiedName~EntityInfoPanelServiceTests"

Passed: 144/144
```

```text
dotnet test src\Tests\ArchitectureTests\ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests"

Passed: 13/13
```

```text
rg -n "SelectionRuntime|SelectionContextRuntime|SelectionControlGroupRuntime|OrderSelectionReference|SelectedEntityProvider|SetSelectedEntityProvider|SelectionRequest|SelectionResponse|SelectionViewRuntime|SelectionViewKeys|CurrentSelection|ViewedSelectionPrimary|SelectedGroupFollowTarget|CameraFollowTargetKind\.Selected|CenterOnSelected|RejectCommandWithoutSelection|EmptySelection|SelectionGate|RewireSelection|SelectionBox|startupSelectedPlayerId|StartupSelectedPlayerId|HasSelectedPlayer|TryGetLocalOwner\(" src mods assets -g "*.cs" -g "*.json" --glob "!**/bin/**" --glob "!**/obj/**"

No matches.
```

```text
rg -n "SelectionGate|OrderSelectionType|selectionType|selectionGate|SelectionRule" docs\architecture\interaction docs\architecture\entity_collection_query_infrastructure.md

No matches.
```

Latest PR review checked on 2026-07-06:

| Review time UTC | Commit | Review status used by this closeout |
|---|---|---|
| 2026-07-06 02:52 | `dc3c1758a8f2dddbb360dc85b58204fc707c3641` | Request-changes-equivalent comment: fail-fast, control-plane, loader, knowledge gate, and benchmark concerns. |
| 2026-07-06 04:26 | `dc62547c047e0d5c2351f7883f15f66d38a3bbbb` | Request-changes-equivalent comment: multi-profile grants, multi-writer domain semantics, partial projection, association churn, reverse-index wrapper, and Team/PlayerOwner sequencing. |
| 2026-07-06 07:15 | `132d742563a2358e72f42a07c7108405701005f3` | Request-changes-equivalent comment: benchmark hardening still missing, old Selection target path still present, and partial-domain budget not fixed in repo tests. |
| 2026-07-06 07:33 | `132d742563a2358e72f42a07c7108405701005f3` | Superseded supplemental audit: at that older commit Selection was still dual-track. Current closeout retires the formal Selection APIs. |
| after latest review | `2417820e9ed225aff3761737f861f234094985d5` | Latest commit folds axis-move into per-scheme `ControlScheme.axisMove`, deletes global `axis_move.json` / `AxisMoveConfig`, and removes the global toggle dual-truth. No submitted review covers this commit yet. |

## Workflow A - Visible UAT And Showcase

Current status: A1 headless/WebApp/DataPlane evidence completed; A1 CEF toggle/revoke framebuffer captured; A2 official launcher binding, WebUI/CEF War3-style bottom command panel, and player-readable Template -> Family -> Ability screenshots captured; A3 official launcher binding and player-readable Raylib world-space timeline screenshots captured; A4 official launcher binding, command-source/scheme evidence, and blink/mixed-dispatch world-space timeline captured; SHOW-3 referee multi-control-domain projection headless evidence and GUI marker/palette evidence completed.

Reason: the current environment can produce real Raylib/CEF framebuffer screenshots and multi-frame Raylib timelines. This pass completes the A1 headless path, launcher binding check, packaged CEF WebApp build, DataPlane topic/command contract, full CEF off -> on -> revoke screenshots, and standard artifacts under `artifacts/acceptance/control-plane-projection-showcase/`. It also completes A2 WebUI/CEF evidence under `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final13_*.png`: the War3-style bottom command panel shows the same three command-source heroes across Template, Family, and Ability aggregation profiles, and the world view now keeps the three units visible with blue/yellow/green profile projection rings. SHOW-3 referee projection headless evidence under `artifacts/acceptance/rfc0065-referee-projection-showcase/` and GUI evidence under `artifacts/rfc0065-visible-uat/control-plane-projection-cef/show3_player_referee_markers2_*.png` show marked=1/grants=2/total=3, then after the second grant revoke the view shrinks to marked=1/grants=1/total=2 while the outsider row remains excluded. A3 now shows Commander plus the two locked targets in world space through pending -> complete -> restored. A4 now shows All Together / One By One / Nearest Top-N with visible world actors and response rings, not only a UI panel.

| Item | Current status | Remaining work |
|---|---|---|
| A1 control-plane projection | Headless path, launcher binding, packaged CEF WebApp assets, DataPlane topic/command, O-key toggle, profile-owned Controls grant/revoke, standard artifacts, and CEF toggle/revoke screenshots are complete. The captured panel visibly shows Proxy Off -> Proxy On -> Proxy Off/revoke, command acknowledgements, owned/proxy/view counts, and ring shrink. | Keep marker performer topology graph-rule conversion as a separate follow-up if RFC owner still requires the final PROV-4b rule form. |
| SHOW-3 referee projection | Headless referee projection evidence is complete, and GUI marker/palette evidence now shows `SHOW-3 Referee`: phase0=1, phase1=2, foreign excluded=1, then `P2 Revoked` with phase1=1/view=2. | None for GUI marker/palette UAT. |
| A2 / SHOW-4 command panel aggregation | Headless/runtime aggregation evidence exists, and the latest WebUI/CEF War3-style 80/160/260 screenshots pass player readability: Template shows 3 hero sheets x 8 commands = 24 tiles with blue world rings; Family shows 8 catalog families such as Projectile with yellow world rings; Ability shows 21 distinct ability definitions with shared Fireball contributor labels and green world rings. | None for the current framebuffer/timeline UAT pass. |
| A3 superweapon context | `SuperweaponContextShowcaseMod`, ability-owned interaction frame, target collection routing, confirm IMC path, standard headless artifacts, formal launcher binding `superweapon_context_showcase`, and a real Raylib world-space pending -> complete/restored timeline are complete. | Add video only if terminal closeout requires video instead of timeline PNGs. |
| A4 pointer intent/dispatch/scheme | Formal launcher binding `interaction_showcase`, readable Raylib world-space timeline, and headless production path evidence exist for right-click ground command -> shared moveTo `OrderBuffer`; tests and screenshots show default command mode, hover ignored, active command group rows, and blink dispatch variants over a mixed command group with visible world actors/rings. | Add video only if terminal closeout requires video instead of timeline PNGs. |

Validated headless evidence from the showcase explorer:

- `ControlPlaneProjectionDataPlaneTests`: 4/4 passed.
- `ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke`: 1/1 passed.
- `EntityCommandPanelShowcaseAcceptanceTests|SuperweaponContextShowcaseAcceptanceTests|Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests`: 67/67 passed in the latest focused closeout filter; `Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests` alone is now 3/3.
- GasTests Release filtered A1/A2/A3/A4 kernel subsets: A1 8, A2 13, A3 25, A4 46 passed.

Latest visible framebuffer evidence captured and cross-checked on 2026-07-09:

| Slice | Binding / selector | Screenshot | Static readability verdict | Boundary |
|---|---|---|---|---|
| A1 / SHOW-2 | `control_plane_projection_showcase` with CEF provider | `artifacts/rfc0065-visible-uat/control-plane-projection-cef/a1_player_command_grant_001_f3000.png`, `a1_player_command_grant_002_f10000.png`, `a1_player_command_grant_003_f15000.png` | PASS: Ally Off -> Ally On -> Ally Off/revoke is readable; Mine/Ally/Total counts change 1/0/1 -> 1/1/2 -> 1/0/1. | Timeline PNGs, not a video file. |
| A2 / SHOW-4 | `entity_command_panel_showcase` WebUI | `artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final13_001_f0080.png`, `a2_webui_war3_final13_002_f0160.png`, `a2_webui_war3_final13_003_f0260.png` | PASS: WebUI/CEF War3-style bottom panel is readable and no longer black-empty in world space; Template shows 24 unit-template slots with blue projection rings, Family shows 8 catalog families with yellow projection rings, and Ability shows 21 distinct ability definitions with shared Fireball contributor labels and green projection rings. | Timeline PNGs, not a video file. |
| A3 / SHOW-1 | `superweapon_context_showcase` | `artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_world_final2_001_f0060.png`, `a3_superweapon_context_world_final2_002_f0140.png`, `a3_superweapon_context_world_final2_003_f0220.png` | PASS: Commander, Arcweaver, and Vanguard are visible in world space; the panel progresses from confirm pending to confirm complete and then targeting restored. | Timeline PNGs, not a video file. |
| A4 / SHOW-5/6 | `interaction_showcase` | `artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_world_final_001_f0045.png`, `a4_blink_mixed_world_final_002_f0135.png`, `a4_blink_mixed_world_final_003_f0225.png` | PASS: All Together, One By One, and Nearest Top-N are readable over the same mixed command group, with visible world actors and response rings. | Timeline PNGs, not a video file. |
| SHOW-3 / referee | `control_plane_projection_showcase` with CEF provider | `artifacts/rfc0065-visible-uat/control-plane-projection-cef/show3_player_referee_markers2_001_f0060.png`, `show3_player_referee_markers2_002_f0160.png`, `show3_player_referee_markers2_003_f0300.png` | PASS: Marked=1/Grants=2/Total=3 is readable, then Grant Revoked shrinks grants to 1 and total to 2 while outsiders stay excluded. | Timeline PNGs, not a video file. |

Old `001` and `002` screenshots are not counted as final evidence. A2 `005` / `006`, A2 `a2_webui_final_*`, A2 `a2_webui_war3_final9_*`, A2 `a2_webui_war3_final10_*`, A2 `a2_webui_war3_final11_*`, and A2 `a2_webui_war3_final12_*` were superseded by the world-space readability pass. A3 `004`, A3 `a3_superweapon_context_readable_*`, A3 `a3_superweapon_context_final_*`, A3 `a3_superweapon_context_world_final_001_*`, A4 `004`, and A4 `a4_blink_mixed_final_*` were superseded by the final world-space Cucumber/player-readability pass. The accepted A2 WebUI/CEF War3-style evidence is `a2_webui_war3_final13_*`; accepted A3/A4 evidence is `a3_superweapon_context_world_final2_*` and `a4_blink_mixed_world_final_*`.

Local ignored summary artifact: `artifacts/rfc0065-visible-uat/visible-uat-summary.md`. It is useful operator evidence, but this tracked closeout does not rely on it as PR-tracked proof.

Subagent E A1 rerun on 2026-07-06:

```text
cd mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/WebApp
npm test

Passed: 4/4 node:test cases.
- standard Ludots facade is the only host transport entrypoint
- missing Ludots facade fails instead of adapting provider globals
- toggleProxy command targets the control plane topic
- production client source does not depend on CEF provider globals
```

```text
cd mods/showcases/control_plane_projection/ControlPlaneProjectionShowcaseMod/WebApp
npm run build

Passed: Vite production build.
Output:
- ../assets/control-plane-app/index.html
- ../assets/control-plane-app/assets/index-B0sADD6R.css
- ../assets/control-plane-app/assets/index-C05TbGjq.js
```

```text
dotnet test src/Tests/WebUiDataPlaneTests/WebUiDataPlaneTests.csproj --filter "FullyQualifiedName~ControlPlaneProjectionDataPlaneTests" --logger "console;verbosity=normal"

Passed: 4/4
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~ControlPlaneProjectionShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 1/1
Note: log confirms this environment has no browser runtime capability, so the dataplane installer stayed inactive instead of pretending a CEF-visible run occurred.
```

SHOW-3 referee projection acceptance on 2026-07-06:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke" --logger "console;verbosity=normal"

Passed: 1/1
Note: headless fixture seeds referee/P1/P2/foreign CommandSource rows, grants Controls(referee->P1Rep/P2Rep), verifies owned=1/proxied=2/foreign=0, revokes Controls(referee->P2Rep), and verifies owned=1/proxied=1/foreign=0.
```

A1 artifacts generated:

- `artifacts/acceptance/control-plane-projection-showcase/battle-report.md`
- `artifacts/acceptance/control-plane-projection-showcase/trace.jsonl`
- `artifacts/acceptance/control-plane-projection-showcase/path.mmd`
- `artifacts/acceptance/control-plane-projection-showcase/visible-checklist.md`

SHOW-4 / Entity command panel rerun on 2026-07-06:

```text
dotnet test src\Tests\GasTests\GasTests.csproj -c Release --filter "FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 2/2
Evidence covered: source registry resolves `gas.collection-ability-slots` to `CollectionGasEntityCommandPanelSource`; Core template/ability profiles and EntityCommandPanelMod by-family fragment are installed; EntityCommandPanelShowcaseMod publishes the `collection.command.source` host collection; toolbar runtime switches Family/Template/Ability profiles; copied slots prove 8 by-family groups, 24 by-template slots, and 21 by-ability groups; visible-UAT auto timeline cycles Template -> Family -> Ability.
```

SHOW-4 artifacts generated:

- `artifacts/acceptance/entity-command-panel-showcase/aggregation-profile-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/battle-report.md`
- `artifacts/acceptance/entity-command-panel-showcase/trace.jsonl`
- `artifacts/acceptance/entity-command-panel-showcase/path.mmd`

A2 WebUI/CEF War3 panel rerun on 2026-07-07:

```text
cd mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/WebApp
npm test

Passed: 3/3
```

```text
cd mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/WebApp
npm run build

Passed: Vite production build into mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/assets/entity-command-panel-app/.
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests"

Passed: 2/2
```

```text
LUDOTS_ENTITY_COMMAND_PANEL_AUTO_PROFILE_TIMELINE=1
LUDOTS_AUTO_EXIT_FRAME=300
LUDOTS_TAKE_SCREENSHOT_FRAMES=80,160,260
launch entity_command_panel_showcase --adapter raylib --build auto

Captured:
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final13_001_f0080.png
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final13_002_f0160.png
- artifacts/rfc0065-visible-uat/entity-command-panel/a2_webui_war3_final13_003_f0260.png

Note: launcher returned nonzero after capture because Chromium logged `Failed opening key Software\Chromium to set usagestats`; CEF rendered and the screenshots were captured.
```

SHOW-1 / superweapon context rerun on 2026-07-07:

```text
dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~SuperweaponContextShowcaseAcceptanceTests"

Passed: 3/3
Evidence covered: ability-owned interaction context frame, target collection routing, confirm IMC path, event-gated completion, default-frame restoration, and visible-UAT auto confirm timeline.
```

Visible UAT rerun on 2026-07-09:

```text
LUDOTS_SUPERWEAPON_CONTEXT_AUTO_CONFIRM_FRAME=90
LUDOTS_TAKE_SCREENSHOT_FRAMES=60,140,220
launch superweapon_context_showcase --adapter raylib

Captured and visually accepted:
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_world_final2_001_f0060.png
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_world_final2_002_f0140.png
- artifacts/rfc0065-visible-uat/superweapon-context/a3_superweapon_context_world_final2_003_f0220.png
```

SHOW-5/6 production path rerun on 2026-07-07:

```text
dotnet test src\Tests\GasTests\GasTests.csproj --no-restore --filter "FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests"

Passed: 3/3
Evidence covered: production startup active `scheme.default`, default command intent, startup command-source collection, hover ambiguity ignored for ground commands, `dispatch.all_together`, shared moveTo order id, and OrderBuffer promotion; visible-UAT default -> WASD scheme timeline; plus hot-switch to `scheme.wasd_move`, WASD `Move` Axis2D input through the authoritative snapshot, and `AxisMoveOrderSystem` moveTo promotion.
```

Visible blink-routing UAT rerun on 2026-07-09:

```text
LUDOTS_INTERACTION_SHOWCASE_AUTO_BLINK_TIMELINE=1
LUDOTS_INTERACTION_SHOWCASE_SEED_HOVER_TARGET=1
LUDOTS_TAKE_SCREENSHOT_FRAMES=45,135,225
launch interaction_showcase --adapter raylib

Captured and visually accepted:
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_world_final_001_f0045.png
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_world_final_002_f0135.png
- artifacts/rfc0065-visible-uat/interaction/a4_blink_mixed_world_final_003_f0225.png
```

SHOW-3 referee projection artifacts generated:

- `artifacts/acceptance/rfc0065-referee-projection-showcase/battle-report.md`
- `artifacts/acceptance/rfc0065-referee-projection-showcase/trace.jsonl`
- `artifacts/acceptance/rfc0065-referee-projection-showcase/path.mmd`

## Workflow B - Benchmark Hardening

Current status: B1 completed in repository tests; B2 current-workstation rerun passed on 2026-07-07 with all reported hot-window allocations at 0 bytes. If the RFC owner requires a dedicated isolated perf host, that external stable-machine rerun remains outside this PR workspace.

This closeout hardens the missing B1 probes as repo tests:

| Probe | Test file | Added or confirmed guard |
|---|---|---|
| Reverse index CopyIncoming specific/any and runtime CollectIncoming wrapper | `src/Tests/GasTests/RelationshipReverseIndexTests.cs` | Already present before this closeout: `bench.reverse_index_*`, `bench.relationship_runtime_collect_*`, ns/source budgets, and 0Alloc assertions. |
| Partial-domain projection | `src/Tests/GasTests/ControlPlaneViewUnitGrantTests.cs` | Upgraded to 50,000 foreign rows, 64 grants, ns/foreign-row budget, bench output, and 0Alloc assertion. |
| Domain-routed ReplaceRouted flatness | `src/Tests/GasTests/DomainRoutedCollectionTests.cs` | Upgraded to 64 domains, 12,288 rows, single-domain comparison, flatness ratio, ns/row budget, bench output, and 0Alloc assertion. |
| AssociationControlProfile single-rep tag flip | `src/Tests/GasTests/AssociationControlProfileTests.cs` | Upgraded from allocation-only to elapsed, evaluated-pair, bench output, and 0Alloc assertions. |
| AssociationControlProfile physical grant/revoke churn | `src/Tests/GasTests/AssociationControlProfileTests.cs` | Already present before this closeout: real Controls EnsureLink/RemoveLink churn after warmup, evaluated-pair bound, elapsed budget, and 0Alloc assertion. |

Latest local validation:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=78.28 ms_per_cycle=1.223 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=25.21 ms_per_cycle=0.788 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=357.75 ns_per_foreign_row=89.4 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=253.72 ms_per_write=7.929 ns_per_row=645.3 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=221.58 ms_per_write=6.924 ns_per_row=563.5 alloc_bytes=0 flatness_ratio_vs_single=0.87
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=89.39 ns_per_source=54.6 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=120.84 ns_per_source=73.8 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=90.18 ns_per_source=55.0 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=100.76 ns_per_source=61.5 alloc_bytes=0
```

B2 current-workstation rerun on 2026-07-07:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=32.99 ms_per_cycle=0.516 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=11.84 ms_per_cycle=0.370 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=125.30 ns_per_foreign_row=31.3 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=66.07 ms_per_write=2.065 ns_per_row=168.0 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=85.10 ms_per_write=2.659 ns_per_row=216.4 alloc_bytes=0 flatness_ratio_vs_single=1.29
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=33.95 ns_per_source=20.7 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=38.80 ns_per_source=23.7 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=34.20 ns_per_source=20.9 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=33.55 ns_per_source=20.5 alloc_bytes=0
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-restore --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=21.45 ms_per_cycle=0.335 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=8.90 ms_per_cycle=0.278 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=61.99 ns_per_foreign_row=15.5 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=61.88 ms_per_write=1.934 ns_per_row=157.4 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=78.08 ms_per_write=2.440 ns_per_row=198.6 alloc_bytes=0 flatness_ratio_vs_single=1.26
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=16.67 ns_per_source=10.2 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=21.42 ns_per_source=13.1 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=16.70 ns_per_source=10.2 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=23.29 ns_per_source=14.2 alloc_bytes=0
```

Conclusion: the B1 benchmark guards pass on this Windows workstation in both Debug/no-build and Release, and every benchmark line reports `alloc_bytes=0`. The Release rerun is the preferred local number set. This is still not claimed as a dedicated lab-machine stability run if that stricter B2 gate is required by reviewers.

## Aggregation Configuration Audit - 2026-07-09

Verdict: A2 aggregation is data-driven, but it is not a general graph JSON with `nodes` / `edges`. The actual PR581 contract is a selector-node DSL: `UI/ability_aggregation_profiles.json` declares `groupBy` expressions, and `AbilityAggregationProfileRegistry` compiles each prefix through a registry-backed selector table at install time. Unknown prefixes fail fast.

| Question | Answer | Evidence |
|---|---|---|
| Are the aggregation profiles hardcoded in the showcase runtime? | No. | Core installs `aggregation.by_template` and `aggregation.by_ability_id` from `assets/Configs/UI/ability_aggregation_profiles.json`; EntityCommandPanelMod adds `aggregation.by_family` from `mods/EntityCommandPanelMod/assets/Configs/UI/ability_aggregation_profiles.json` with `groupBy: catalog.castFamily`. |
| Is Family mapping hardcoded as `Fireball -> Projectile`? | No. | Family grouping reads ability `catalogTags` through `TagRegistry`; the showcase data assigns `castFamily.projectile`, `castFamily.mobility`, `castFamily.defense`, and the remaining families in `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/assets/GAS/abilities.json`. |
| Is Template mapping hardcoded by hero names? | No for the aggregation key. | `template.id` requires `EntityTemplateKeyRef` and groups by owner unit template plus slot index; the showcase's `assets/Entities/templates.json` supplies the unit-template ability layout. |
| Is Ability mapping hardcoded by display labels? | No. | `ability.id` groups by ability definition id; repeated labels collapse only when they share the same ability definition id, not because the text matches. |
| Is this a graph-node DSL? | Not in the broad GAS-graph sense. | It is a small selector-node DSL (`catalog.*`, `template.id`, `ability.id`, plus registered prefixes). Calling it a generic graph DSL would be inaccurate. |

The remaining literal strings in A2 are scenario labels, UI captions, and UAT assertions. They describe the showcase evidence; they do not decide aggregation membership.

Subagent D rerun on 2026-07-06:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~RelationshipReverseIndexTests|FullyQualifiedName~ControlPlaneViewUnitGrantTests|FullyQualifiedName~DomainRoutedCollectionTests|FullyQualifiedName~AssociationControlProfileTests" --logger "console;verbosity=normal"

Passed: 41/41
bench.association_profile_tag_flip reps=64 profiles=4 toggle_cycles=64 elapsed_ms=27.55 ms_per_cycle=0.430 alloc_bytes=0 evaluated_pairs=16128
bench.association_profile_churn reps=64 toggle_cycles=32 elapsed_ms=10.13 ms_per_cycle=0.317 alloc_bytes=0 evaluated_pairs=8064
bench.control_plane_partial_view foreign_rows=50000 direct_grants=64 returned=65 iterations=80 elapsed_ms=137.99 ns_per_foreign_row=34.5 alloc_bytes=0
bench.domain_routed_write_single domains=1 rows=12288 iterations=32 elapsed_ms=77.03 ms_per_write=2.407 ns_per_row=195.9 alloc_bytes=0
bench.domain_routed_write domains=64 rows=12288 iterations=32 elapsed_ms=93.76 ms_per_write=2.930 ns_per_row=238.4 alloc_bytes=0 flatness_ratio_vs_single=1.22
bench.reverse_index_specific sources=4096 iterations=400 elapsed_ms=62.64 ns_per_source=38.2 alloc_bytes=0
bench.reverse_index_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=66.85 ns_per_source=40.8 alloc_bytes=0
bench.relationship_runtime_collect_specific sources=4096 iterations=400 elapsed_ms=73.44 ns_per_source=44.8 alloc_bytes=0
bench.relationship_runtime_collect_any sources=4096 edge_types_per_source=4 iterations=400 elapsed_ms=36.44 ns_per_source=22.2 alloc_bytes=0
```

## Superseded Selection Retirement Audit - 2026-07-07

Current verdict: formal Selection APIs are retired repo-wide in the current closeout pass. The text below records the old 2026-07-07 audit method and why the final pass had to remove the remaining formal APIs rather than documenting a dual-track state.

Search commands used for this pass:

```text
rg -n "Selection|CurrentSelection|Selected|selection fallback|fallback" src/Core/Input/Selection src/Core/Engine/GameEngine.cs mods/CoreInputMod mods/showcases src/Core/MassNavigation src/Core/Presentation src/Core/Gameplay/Camera mods/EntityCommandPanelMod --glob '!**/bin/**' --glob '!**/obj/**'
rg -n "command\.source|command-source|CommandSource|collection\.command\.source|EntityCollectionKeys\.CommandSource|CurrentSelection|SelectionRuntime|LivePrimary|fallback|Selected entity" src/Core/Input/Orders mods/CoreInputMod mods/EntityCommandPanelMod mods/showcases/interaction src/Tests/GasTests --glob '!**/bin/**' --glob '!**/obj/**'
rg -n "Selected|Selection|SelectionContextRuntime|SelectionViewRuntime|LivePrimary" src/Core/Gameplay/Camera src/Core/Presentation/Minimap src/Core/MassNavigation mods/showcases/interaction mods/capabilities/participant_view --glob '!**/bin/**' --glob '!**/obj/**'
```

| Audit question | Finding | Evidence |
|---|---|---|
| Does the RFC-0065 ground `Command` command-source path depend on Selection? | No for the routed `Command` slice when RFC-0065 services are installed. It reads the active interaction frame, `ControlSchemeRuntime`, command intent profiles, dispatch profiles, and `EntityCollectionStore`; it copies actors from the active collection and never calls an implicit selected-provider path. | `src/Core/Input/Orders/InputOrderMappingSystem.cs` routes command actions to `SubmitRfc0065Command`; that method resolves `frame.ActiveCollectionKeyId`, copies actors from `EntityCollectionStore`, routes through `CommandIntentProfileRegistry.RouteGroup`, dispatches through `CastDispatchProfileRegistry.SelectDispatchTargets`, then submits orders. `mods/CoreInputMod/Systems/LocalOrderSourceHelper.cs` fail-fast configures these services instead of silently falling back. |
| Is repo-wide Selection retired? | Yes in the current closeout pass. | The old `src/Core/Input/Selection/*` infrastructure is deleted; `GameEngine` no longer registers formal Selection services; command authority uses `EntityCollectionStore` and explicit command-source collections. |
| Is the EntityCommandPanel command-source path selection-free? | The aggregation source is command-source based. | `mods/EntityCommandPanelMod/Runtime/CollectionGasEntityCommandPanelSource.cs` resolves `context.TargetEntity + config.CollectionKey` through `EntityCollectionStore`; no `SelectionRuntime` read is present in that source. The showcase host still has to publish `collection.command.source`, but the panel source itself does not use Selection as its command source. |
| Which old Selection consumers were migrated? | Acquisition/view/control-group, presentation readers, camera/minimap readers, participant-view/showcase projections, and legacy skill/cast tests now resolve through explicit entity collections or command-source helpers. | Current production and test scans ban `SelectionRuntime`, `SelectionSetKeys`, `SelectionViewKeys`, `SelectionContextRuntime`, `SelectionViewRuntime`, `SelectionControlGroupRuntime`, `OrderSelectionReference`, and request/response queues. |

Superseded fallback / dual-truth findings:

- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs` was tightened to command-source collection helpers and no longer bridges formal Selection services.
- `src/Core/Input/Orders/InputOrderMappingSystem.cs` must not rebuild selected-provider fallback semantics; command actions route through explicit command-source collections, and legacy skill/cast paths now expose neutral `CollectionPrimaryEntityProvider` / `CollectionEntityListProvider` contracts.
- `mods/showcases/interaction/InteractionShowcaseMod/Runtime/InteractionShowcaseRuntime.cs` seeds command-source rows directly for the RFC-0065 command path.
- `src/Core/MassNavigation/**` no longer references Selection/CommandSource/InteractionContextStack authority APIs. It consumes explicit `OrderBuffer` move orders and command actor spans; remaining MassNavigation follow-up work is the separate `PlayerOwner`/`Team` domain migration.
- `mods/capabilities/participant_view/**`, minimap, camera, entity-info, and UI readers are collection readers; they must not depend on formal Selection APIs.

Follow-up selection-boundary tightening completed after this audit:

- `InputOrderMappingSystem` no longer exposes `SelectedEntityProvider` / `SetSelectedEntityProvider` or selected actor scratch names; callers inject `CollectionPrimaryEntityProvider` and `CollectionEntityListProvider`.
- `CommandSourceAcquisitionSystem` publishes `OnEntityAcquired`; CoreInputMod exposes `CommandSourceAcquiredCallbacks` instead of `EntitySelectionCallbacks`.
- Camera follow targets use `EntityCollectionPrimary` / `EntityCollectionGroup` and `EntityCollection*FollowTarget` classes. Config assets were migrated to those names without keeping old enum aliases.
- Minimap runtime centers on the command-source primary through explicit entity collections; MassNavigation result/method names use `CommandActors`.
- EntityInfo uses explicit `EntityCollection` panel targets, and the generic move-path projection is `CommandActorMovePathPresentationSystem`.
- `CommandSourceDragOverlaySystem` handles the UI drag rectangle using explicit command-source acquisition config; it is not an authority API and does not provide command-source membership by itself.

Remaining guard tasks after repo-wide formal Selection retirement:

- Keep command-source-only provider names explicit and forbid implicit selected-provider fallbacks in `Command` actions.
- Keep a focused guard that a `Command` action with missing active command-source collection / missing active intent fails instead of routing through any retired formal Selection name.
- Keep the interaction showcase command-source direct-seeding path guarded; do not introduce any `LivePrimary -> collection.command.source` bridge.
- Keep MassNavigation on explicit OrderQueue ingestion; do not migrate it to CommandSource or InteractionContext reads.
- Keep minimap, camera, participant-view, skill-bar, selection-box, entity-info, and showcase readers on explicit entity collections.
- Keep architecture bans that prevent reintroducing formal Selection APIs.

Small fixes made during this audit:

- `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs` no longer uses `EntityCollectionStore.CopyEntities` in the Issue200-guarded input/knowledge consumer path; it reads the active command-source view with `TryGetEntityAt` per row.
- `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/Runtime/EntityCommandPanelShowcaseRuntime.cs` makes the aggregation toolbar visible after publishing the showcase `collection.command.source`, restoring the SHOW-4 runtime switch acceptance path.
- `mods/showcases/entity_command_panel/EntityCommandPanelShowcaseMod/DataPlane/EntityCommandPanelShowcaseDataPlane.cs` imports the existing GAS `AbilityIdRegistry` instead of relying on an unresolved name.
- `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs` now fails fast when `exec.clockId`, required timeline fields, gate payloads, graph ids, effect ids, tag strings, caller param entries, toggle effects, or presentation mode overrides are malformed; non-object `exec.items[]` and more than `AbilityExecSpec.MAX_ITEMS` are rejected instead of skipped or truncated.
- `mods/showcases/superweapon_context/SuperweaponContextShowcaseMod/Runtime/SuperweaponContextShowcaseRuntime.cs` now starts the showcase ability through `OrderQueue.TryEnqueue` with `OrderArgs.I0` instead of hardcoding an order id and calling `OrderBuffer.SetActiveDirect`; cleanup goes through `OrderSubmitter.CancelAll`.
- `src/Libraries/Arch.Extended/Arch.Relationships/Relationship.cs` no longer compiles reflection accessors over `SortedList` private `values` / `version` fields. It keeps the public `SortedList` backing store required by persistence and uses public APIs after the local binary key lookup.

Validation after the second-audit patch:

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests" --logger "console;verbosity=normal"

Passed: 8/8
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-build --filter "FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests" --logger "console;verbosity=normal"

Passed: 77/77
```

Final PR581 command-authority closeout validation:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-restore --filter "FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~InputOrderConvergenceValidationTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~InputOrderContractTests" --logger "console;verbosity=normal"

Passed: 59/59
```

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --no-restore --filter "FullyQualifiedName~MassNavigationLocalCommandInputSystemTests|FullyQualifiedName~MassNavigationRouteExecutionContractTests|FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests" --logger "console;verbosity=normal"

Passed: 28/28
```

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests" --logger "console;verbosity=normal"

Passed: 10/10
```

```text
rg -n "SelectionRuntime|SelectionSetKeys|LivePrimary|CurrentSelection|EntityCollectionKeys\.CommandSource|InteractionContextStack|OrderSelectionReference|MassNavigationSelectionSync|MassNavigationLocalCommandInputSystem" src/Core/MassNavigation

No matches.
```

Final 2026-07-08 validation after Superweapon OrderQueue and Relationship reflection fixes:

```text
dotnet test src/Tests/WebUiDataPlaneTests/WebUiDataPlaneTests.csproj --no-restore --filter "FullyQualifiedName~ControlPlaneProjectionDataPlaneTests"

Passed: 4/4
```

```text
dotnet test src/Tests/GasTests/GasTests.csproj --no-restore --filter "FullyQualifiedName~ControlPlaneProjectionShowcaseAcceptanceTests|FullyQualifiedName~ControlPlaneRefereeProjectionShowcase_ProjectsTwoControlDomainsAndShrinksAfterRevoke|FullyQualifiedName~EntityCommandPanelShowcaseAcceptanceTests|FullyQualifiedName~SuperweaponContextShowcaseAcceptanceTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests|FullyQualifiedName~InputOrderContractTests"

Passed: 29/29
```

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests"

Passed: 10/10
```

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --no-restore --filter "FullyQualifiedName~MassNavigationLocalCommandInputSystemTests|FullyQualifiedName~MassNavigationRouteExecutionContractTests|FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests"

Passed: 28/28
```

```text
DOTNET_ROLL_FORWARD=Major dotnet test src/Libraries/Arch.Extended/Arch.Relationships.Tests/Arch.Relationships.Tests.csproj --no-restore

Passed: 5/5
Note: roll-forward is needed on this workstation because .NET 7 runtime is not installed.
```

Final 2026-07-08 validation after external-entity / collection-provider selection-boundary tightening:

```text
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~InputOrderContractTests|FullyQualifiedName~CommandActorMovePathPresentationSystemTests|FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~InteractionSelectionConvergenceTests|FullyQualifiedName~EffectPresetInteractionModeTests|FullyQualifiedName~Rfc0065ShowcaseWorkflowBoundaryAcceptanceTests"

Passed: 86/86
```

```text
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --no-build --filter "FullyQualifiedName~EntityInfoPanelServiceTests|FullyQualifiedName~MassNavigationLocalCommandInputSystemTests|FullyQualifiedName~MassNavigationAuthoredAgentBindingIncrementalTests"

Passed: 33/33
```

```text
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj --no-build --filter "FullyQualifiedName~CameraRuntimeConvergenceTests"

Passed: 14/14
```

```text
dotnet test src/Tests/ThreeCTests/ThreeCTests.csproj --no-build --filter "FullyQualifiedName~SharedThreeCProfilesModTests"

Passed: 1/1
```

```text
dotnet test src/Tests/ArchitectureTests/ArchitectureTests.csproj --filter "FullyQualifiedName~Rfc0065InteractionCastingBoundaryContractTests"

Passed: 13/13
```

```text
rg -n -e "SelectedEntityProvider" -e "SetSelectedEntityProvider" -e "SetSelectedEntityListProvider" -e "OnEntitySelected" -e "EntitySelectionCallbacks" -e "CurrentSelectionView" -e "ViewedSelectionPrimary" -e "SelectedGroupFollowTarget" -e "CameraFollowTargetKind\.Selected" -e "CenterOnSelected" -e "EmptySelection" -e "RejectCommandWithoutSelection" src/Core mods/CoreInputMod mods/MobaDemoMod mods/EntityCommandPanelMod mods/capabilities mods/showcases/interaction

No matches.
```

Known non-regression noise during broad local validation:

- `dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter "...|FullyQualifiedName~Minimap"` also selected `PerformerDynamicWorkerBenchmarkTests` minimap large-world benchmarks; those failed with 30k screen marker count `0`, outside this selection-boundary patch.
- Broad `CameraAcceptanceModTests` / `CameraShowcaseModTests` include existing fixture failures around missing `LocalPlayerEntity`, unmounted UI panel, and inactive camera follow state. The core `CameraRuntimeConvergenceTests` and config parse test above pass after the follow-target enum rename.

```text
git diff --check
scripts/validate-docs.ps1

Passed. `git diff --check` reported only the Windows LF-to-CRLF warning for Relationship.cs.
```

## Workflow C - Migration Work

Current status: discovery complete; broad migrations intentionally not performed in PR581.

| Item | Status | Reason |
|---|---|---|
| C1 CTRL-3 consumer migration and embodied `PlayerOwner`/`Team` deletion | Not safe in PR581 | Actual consumers span GAS targeting, projectile hit checks, queries, AI predicates, input/Selection, presentation, visibility, participant view, MassNavigation, lifecycle, save, and spawn. This must be split into independent breaking PRs, each with post-migration zero-old-component assertions. |
| C2 input main-chain wiring | Partially wired; terminal migration blocked | The RFC-0065 `Command` ground path now resolves through `CommandIntentArbiter` -> `CommandIntentProfileRegistry.RouteGroup` -> `CastDispatchProfileRegistry.SelectDispatchTargets` -> `OrderQueue`, with focused acceptance coverage. The broader skill/cast fan-out path and `InteractionModeType` retirement still depend on PR #535 vs #577 arbitration and must stay as follow-up migration work. |
| C3 presentation/provider follow-ups | Not safe in PR581 | `VisibilityCondition` graph emit still throws on graph visibility and still needs production wiring. SHOW-3 GUI marker/referee/palette UAT is complete and is not a remaining visible-evidence blocker. |
| C4 INT-8, M10, DOC-1 | Deferred by design | Tag/stance knowledge-fact projection is new infrastructure; replay acceptance needs a replay harness; gitbook rewrite waits for RFC acceptance. |

Selection double-check result: formal Selection APIs are retired repo-wide in the current closeout pass. Local source audit must stay clean for `SelectionRuntime`, `SelectionSetKeys`, `SelectionViewKeys`, `SelectionContextRuntime`, `SelectionViewRuntime`, `SelectionControlGroupRuntime`, `SelectionRequest`, `SelectionResponse`, `OrderSelectionReference`, and related formal-service globals. The RFC-0065 ground `Command` route and MassNavigation core boundary remain Selection-free command-authority slices.

Workflow C source blockers rechecked on 2026-07-07:

- C1 cannot delete embodied `PlayerOwner`/`Team` yet. Live consumers still include `src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs`, `src/Core/Gameplay/GAS/Systems/ProjectileRuntimeSystem.cs`, `src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs`, `src/Core/Gameplay/GAS/BuiltinHandlers.cs`, `src/Core/Gameplay/Lifecycle/LifecycleSnapshot.cs`, `src/Core/Gameplay/Lifecycle/EntityLifecycleAtomicOps.cs`, `src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs`, `src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs`, `src/Core/ParticipantVisibility/DynamicParticipantVisibilityPublisher.cs`, `mods/capabilities/participant_view/ParticipantViewCapabilityMod/Runtime/ParticipantViewProjection.cs`, `mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs`, and the MassNavigation runtime/systems under `src/Core/MassNavigation/`.
- C2 cannot retire `InteractionModeType` in PR581. Live consumers still include `src/Core/Input/Orders/InputOrderMapping.cs`, `src/Core/Input/Orders/InputOrderMappingSystem.cs`, `src/Core/Gameplay/GAS/AbilityDefinitionRegistry.cs`, `src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs`, `mods/CoreInputMod/ViewMode/ViewModeManager.cs`, `mods/EntityCommandPanelMod/Runtime/GasEntityCommandPanelSource.cs`, `mods/EntityCommandPanelMod/UI/EntityCommandPanelController.cs`, and `mods/MobaDemoMod/Systems/MobaLocalOrderSourceSystem.cs`.
- C3 graph visibility emit remains intentionally fail-fast, not wired. `src/Core/Presentation/Systems/PerformerEmitSystem.cs` still throws when `PerformerDefinition.VisibilityCondition.GraphProgramId > 0`; wiring it requires a per-viewer graph visibility contract. SHOW-3 marker/referee/palette visible UAT is already complete.
- Formal Selection runtime/view/control-group services are retired. `GameEngine`, CoreInputMod, showcase code, minimap/camera/UI readers, and tests must use `EntityCollectionStore` / explicit collection keys without formal Selection fallback.

Safe follow-up slices:

- Continue moving `CommandSourceEligibility.CanAcquire` and related consumers off embodied `Team` as narrow headless PRs.
- Migrate GAS targeting/projectile/query/AI consumers to `ControlDomainQuery` and `DomainStanceQuery`.
- Migrate participant visibility and ParticipantView to participant bindings and relationship topology.
- Migrate presentation phase/palette after C3 topology and palette contracts are complete.
- Continue MassNavigation's separate `PlayerOwner`/`Team` domain migration after command intake and OrderQueue ownership remain guarded; MassNavigation core now consumes explicit move orders and has no Selection/CommandSource/InteractionContextStack authority reads.

Do not delete `PlayerOwner`, `Team`, `TeamManager`, or `InteractionModeType` in PR581 without the corresponding migration and architecture bans. Formal Selection APIs are already retired and must not be reintroduced.

## Closeout Decision

Completed now:

- Latest PR review rechecked.
- Handoff Workflow A/B/C remapped to completed, blocked, and follow-up buckets.
- A1 control-plane projection headless/WebApp/DataPlane evidence completed with standard artifacts; CEF toggle/revoke screenshots captured and player-readable.
- A2/A3/A4 formal launcher bindings added and verified through `run-mod-launcher` resolve.
- A2 WebUI/CEF War3-style bottom command panel captured and player-readable for Template -> Family -> Ability at frames 80/160/260, with world-space profile projection rings visible in all three frames.
- A3 and A4 real Raylib framebuffer timelines captured through the formal binding selectors and cleaned into player-readable Gherkin-style screenshots.
- A3 and A4 headless acceptance artifacts regenerated through tests with launcher binding guards.
- A4 headless acceptance now covers default right-click dispatch, startup CommandSource rows, hover ambiguity, visible-UAT scheme timeline, and `scheme.wasd_move` WASD hot-switch through the production input snapshot/order path; blink/mixed-selection UI timeline screenshots cover all_together, one_by_one, and nearest_top_n.
- SHOW-3 referee multi-control-domain projection headless evidence completed with standard artifacts; GUI marker/palette recording now shows phase0/phase1/foreign exclusion and revoke shrink.
- Selection is retired from the RFC-0065 ground `Command` authority path: configured command routing fail-fasts when partially wired, consumes missing active intent without legacy fallback, resolves actors from the active command-source collection, and does not call selected-provider fallbacks.
- MassNavigation core is decoupled from input arbitration: it consumes explicit `OrderBuffer` move orders, no longer references Selection/CommandSource/InteractionContextStack/`OrderSelectionReference`, and its self-contained move orders encode a null selection reference through the shared `OrderArgs` factory.
- B1 benchmark hardening completed for the missing repo tests.
- B2 current-workstation Debug and Release reruns passed 41/41 with all reported benchmark windows at `alloc_bytes=0`; Release is the preferred local evidence set.
- Selection status corrected and double-checked: formal Selection APIs are retired; user-facing plain-language "selection" wording may remain only as shorthand for explicit entity collections. The command-authority slices are Selection-free.

Still open by dependency:

- Full video recordings where required beyond the accepted A1/A2/A3/A4/SHOW-3 timeline PNG evidence.
- Dedicated isolated B2 perf host rerun, if reviewers require more than the current-workstation Debug/Release evidence above.
- C1/C2/C3/C4 migrations and RFC/gitbook terminal-state work.
